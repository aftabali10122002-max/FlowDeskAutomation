using System.Security.Cryptography;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Enums;

namespace FlowDisk.Api.Attachments;

public sealed class RequestAttachmentScanWorker(
    IServiceScopeFactory scopeFactory,
    IPrivateFileStorage storage,
    IAttachmentMalwareScanner scanner,
    RequestAttachmentOptions options,
    ILogger<RequestAttachmentScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.ScanIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (
                stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Request attachment scan batch failed with {FailureType}.",
                    exception.GetType().Name);
            }
        }
    }

    public async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IRequestAttachmentRepository>();
        for (var index = 0; index < options.ScanBatchSize; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nowUtc = DateTime.UtcNow;
            var claimed = await repository.ClaimPendingScansAsync(
                nowUtc,
                nowUtc.AddSeconds(options.ScanLeaseSeconds),
                options.MaximumScanAttempts,
                batchSize: 1);
            var item = claimed.SingleOrDefault();
            if (item is null)
            {
                break;
            }

            await ProcessItemAsync(repository, item, cancellationToken);
        }
    }

    private async Task ProcessItemAsync(
        IRequestAttachmentRepository repository,
        RequestAttachmentScanWorkItem item,
        CancellationToken cancellationToken)
    {
        AttachmentScanResult? result = null;
        var failureCode = await ScanFailureAsync(
            item,
            value => result = value,
            cancellationToken);
        var completedAtUtc = DateTime.UtcNow;

        if (failureCode is not null)
        {
            var retryExhausted =
                item.ScanAttempts >= options.MaximumScanAttempts;
            DateTime? nextAttemptAtUtc = retryExhausted
                ? null
                : completedAtUtc.AddSeconds(GetRetryDelaySeconds(
                    item.ScanAttempts));
            var saved = await repository.FailScanAsync(
                item.AttachmentId,
                item.LeaseToken,
                retryExhausted ? "retry_exhausted" : failureCode,
                completedAtUtc,
                nextAttemptAtUtc);
            if (!saved)
            {
                logger.LogDebug(
                    "Ignored stale scan failure result for attachment {AttachmentId}.",
                    item.AttachmentId);
            }
            else
            {
                logger.LogWarning(
                    "Attachment scan attempt failed for attachment {AttachmentId} with code {FailureCode}.",
                    item.AttachmentId,
                    retryExhausted ? "retry_exhausted" : failureCode);
            }

            return;
        }

        var scanResult = result
            ?? throw new InvalidOperationException(
                "The scanner completed without a result or failure code.");
        var completed = await repository.CompleteScanAsync(
            item.AttachmentId,
            item.LeaseToken,
            scanResult.Status,
            scanResult.ScannerName,
            completedAtUtc);
        if (!completed)
        {
            logger.LogDebug(
                "Ignored stale scan result for attachment {AttachmentId}.",
                item.AttachmentId);
        }
    }

    private async Task<string?> ScanFailureAsync(
        RequestAttachmentScanWorkItem item,
        Action<AttachmentScanResult> setResult,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var verificationStream = await storage.OpenReadAsync(
                PrivateFileArea.RequestAttachments,
                item.StorageKey,
                item.Extension,
                cancellationToken);
            if (verificationStream is null)
            {
                return "storage_content_missing";
            }

            var storedHash = Convert.ToHexString(
                await SHA256.HashDataAsync(
                    verificationStream,
                    cancellationToken));
            if (!string.Equals(
                storedHash,
                item.Sha256,
                StringComparison.OrdinalIgnoreCase))
            {
                return "hash_mismatch";
            }

            await using var scanStream = await storage.OpenReadAsync(
                PrivateFileArea.RequestAttachments,
                item.StorageKey,
                item.Extension,
                cancellationToken);
            if (scanStream is null)
            {
                return "storage_content_missing";
            }

            var scan = await scanner.ScanAsync(
                scanStream,
                item.MediaType,
                item.Sha256,
                cancellationToken);
            if (scan.Status == AttachmentScanStatus.Clean &&
                !string.IsNullOrWhiteSpace(scan.ScannerName) &&
                scan.ScannerName.Length <= 100)
            {
                setResult(scan);
                return null;
            }

            if (scan.Status == AttachmentScanStatus.Rejected &&
                !string.IsNullOrWhiteSpace(scan.ScannerName) &&
                scan.ScannerName.Length <= 100)
            {
                setResult(scan);
                return null;
            }

            return "invalid_scanner_response";
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return "scan_timeout";
        }
        catch (AttachmentScannerException exception)
        {
            return exception.FailureCode;
        }
        catch (IOException)
        {
            return "storage_content_missing";
        }
        catch (CryptographicException)
        {
            return "hash_mismatch";
        }
        catch (Exception)
        {
            return "scan_failed";
        }
    }

    private int GetRetryDelaySeconds(int attempt)
    {
        var multiplier = Math.Pow(2, Math.Max(0, attempt - 1));
        return (int)Math.Min(
            options.ScanRetryMaximumSeconds,
            options.ScanRetryBaseSeconds * multiplier);
    }
}
