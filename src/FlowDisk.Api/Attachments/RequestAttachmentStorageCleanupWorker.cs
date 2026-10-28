using FlowDesk.Application.Interfaces;
using FlowDisk.Api.Attachments;

namespace FlowDisk.Api.Notifications;

public sealed class RequestAttachmentStorageCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IPrivateFileStorage storage,
    RequestAttachmentOptions options,
    ILogger<RequestAttachmentStorageCleanupWorker> logger)
    : BackgroundService
{
    private string? orphanContinuationToken;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.CleanupIntervalSeconds));
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Request attachment storage cleanup failed with {FailureType}.",
                    exception.GetType().Name);
            }
        }
    }

    public async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IRequestAttachmentRepository>();
        var pending = await repository.GetPendingStorageCleanupAsync(
            options.CleanupBatchSize,
            DateTime.UtcNow);
        foreach (var attachment in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await storage.DeleteAsync(
                    PrivateFileArea.RequestAttachments,
                    attachment.StorageKey,
                    attachment.Extension,
                    cancellationToken);
                attachment.MarkStorageCleanupComplete();
                await repository.SaveChangesAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Storage cleanup remains pending for a request attachment after {FailureType}.",
                    exception.GetType().Name);
            }
        }

        var orphanCutoff = DateTime.UtcNow.AddHours(
            -options.OrphanFileGraceHours);
        var page = await storage.GetEntriesAsync(
            PrivateFileArea.RequestAttachments,
            orphanContinuationToken,
            options.CleanupBatchSize,
            cancellationToken);
        orphanContinuationToken = page.ContinuationToken;
        foreach (var candidate in page.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.LastModifiedAtUtc >= orphanCutoff)
            {
                continue;
            }

            try
            {
                if (await repository.IsStorageKeyReferencedAsync(
                    candidate.StorageKey))
                {
                    continue;
                }

                await storage.DeleteAsync(
                    PrivateFileArea.RequestAttachments,
                    candidate.StorageKey,
                    candidate.Extension,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Orphan attachment file cleanup will be retried after {FailureType}.",
                    exception.GetType().Name);
            }
        }
    }
}
