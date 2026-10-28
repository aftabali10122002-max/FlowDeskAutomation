using System.Data;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Repositories;

public sealed class RequestAttachmentRepository(AppDbContext context)
    : IRequestAttachmentRepository
{
    public Task<List<RequestAttachment>> GetForRequestAsync(int requestId)
    {
        return context.RequestAttachments
            .AsNoTracking()
            .Include(x => x.UploadedByUser)
            .Where(x => x.RequestId == requestId && x.DeletedAtUtc == null)
            .OrderBy(x => x.UploadedAtUtc)
            .ToListAsync();
    }

    public Task<RequestAttachment?> GetForRequestByIdAsync(
        int requestId,
        long attachmentId,
        bool includeDeleted = false)
    {
        var query = context.RequestAttachments
            .Include(x => x.UploadedByUser)
            .Where(x =>
                x.RequestId == requestId &&
                x.Id == attachmentId);
        if (!includeDeleted)
        {
            query = query.Where(x => x.DeletedAtUtc == null);
        }

        return query.FirstOrDefaultAsync();
    }

    public Task<RequestAttachment?> GetByClientUploadIdAsync(
        Guid clientUploadId)
    {
        return context.RequestAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClientUploadId == clientUploadId);
    }

    public Task<List<RequestAttachment>> GetPendingStorageCleanupAsync(
        int batchSize,
        DateTime nowUtc)
    {
        return context.RequestAttachments
            .Where(x =>
                x.StorageCleanupPending &&
                (!x.ScanLeaseExpiresAtUtc.HasValue ||
                 x.ScanLeaseExpiresAtUtc <= nowUtc))
            .OrderBy(x => x.DeletedAtUtc)
            .Take(batchSize)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<RequestAttachmentScanWorkItem>>
        ClaimPendingScansAsync(
            DateTime nowUtc,
            DateTime leaseExpiresAtUtc,
            int maximumAttempts,
            int batchSize)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var exhaustedIds = await context.RequestAttachments
            .AsNoTracking()
            .Where(x =>
                (x.ScanStatus == AttachmentScanStatus.Pending ||
                 x.ScanStatus == AttachmentScanStatus.NotScanned) &&
                x.DeletedAtUtc == null &&
                x.ScanAttempts >= maximumAttempts &&
                x.ScanLeaseExpiresAtUtc <= nowUtc)
            .OrderBy(x => x.Id)
            .Take(batchSize)
            .Select(x => x.Id)
            .ToListAsync();
        if (exhaustedIds.Count > 0)
        {
            await context.RequestAttachments
                .Where(x => exhaustedIds.Contains(x.Id))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        x => x.ScanStatus,
                        AttachmentScanStatus.Pending)
                    .SetProperty(x => x.ScanFailureCode, "retry_exhausted")
                    .SetProperty(
                        x => x.NextScanAttemptAtUtc,
                        (DateTime?)null)
                    .SetProperty(x => x.ScanLeaseToken, (Guid?)null)
                    .SetProperty(
                        x => x.ScanLeaseExpiresAtUtc,
                        (DateTime?)null));
        }

        var candidates = await context.RequestAttachments
            .AsNoTracking()
            .Where(x =>
                (x.ScanStatus == AttachmentScanStatus.Pending ||
                 x.ScanStatus == AttachmentScanStatus.NotScanned) &&
                x.DeletedAtUtc == null &&
                x.ScanAttempts < maximumAttempts &&
                (!x.NextScanAttemptAtUtc.HasValue ||
                 x.NextScanAttemptAtUtc <= nowUtc) &&
                (!x.ScanLeaseExpiresAtUtc.HasValue ||
                 x.ScanLeaseExpiresAtUtc <= nowUtc))
            .OrderBy(x => x.NextScanAttemptAtUtc)
            .ThenBy(x => x.Id)
            .Take(batchSize)
            .ToListAsync();

        var claimed = new List<RequestAttachment>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var attachment = context.RequestAttachments.Local
                .FirstOrDefault(x => x.Id == candidate.Id);
            if (attachment is null)
            {
                attachment = candidate;
                context.RequestAttachments.Attach(attachment);
            }
            else
            {
                await context.Entry(attachment).ReloadAsync();
            }

            attachment.ClaimScan(
                Guid.NewGuid(),
                nowUtc,
                leaseExpiresAtUtc);
            claimed.Add(attachment);
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return claimed.Select(x => new RequestAttachmentScanWorkItem(
            x.Id,
            x.StorageKey,
            x.Extension,
            x.MediaType,
            x.Sha256,
            x.ScanLeaseToken!.Value,
            x.ScanAttempts)).ToList();
    }

    public async Task<bool> CompleteScanAsync(
        long attachmentId,
        Guid leaseToken,
        AttachmentScanStatus status,
        string? scannerName,
        DateTime scannedAtUtc)
    {
        if (leaseToken == Guid.Empty ||
            status is not AttachmentScanStatus.Clean and
                not AttachmentScanStatus.Rejected ||
            string.IsNullOrWhiteSpace(scannerName) ||
            scannerName.Length > 100)
        {
            throw new ArgumentException("The scan result is invalid.");
        }

        var updated = await context.RequestAttachments
            .Where(x =>
                x.Id == attachmentId &&
                x.ScanStatus == AttachmentScanStatus.Pending &&
                x.DeletedAtUtc == null &&
                x.ScanLeaseToken == leaseToken &&
                x.ScanLeaseExpiresAtUtc > scannedAtUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ScanStatus, status)
                .SetProperty(x => x.ScannerName, scannerName)
                .SetProperty(x => x.ScannedAtUtc, scannedAtUtc)
                .SetProperty(
                    x => x.ScanFailureCode,
                    status == AttachmentScanStatus.Rejected
                        ? "malware_detected"
                        : null)
                .SetProperty(x => x.NextScanAttemptAtUtc, (DateTime?)null)
                .SetProperty(x => x.ScanLeaseToken, (Guid?)null)
                .SetProperty(x => x.ScanLeaseExpiresAtUtc, (DateTime?)null));
        return updated == 1;
    }

    public async Task<bool> FailScanAsync(
        long attachmentId,
        Guid leaseToken,
        string failureCode,
        DateTime failedAtUtc,
        DateTime? nextAttemptAtUtc)
    {
        if (leaseToken == Guid.Empty ||
            failureCode is not "scanner_unavailable" and
                not "scan_timeout" and
                not "invalid_scanner_response" and
                not "hash_mismatch" and
                not "storage_content_missing" and
                not "scan_failed" and
                not "retry_exhausted")
        {
            throw new ArgumentException("The scan failure is invalid.");
        }

        var updated = await context.RequestAttachments
            .Where(x =>
                x.Id == attachmentId &&
                x.ScanStatus == AttachmentScanStatus.Pending &&
                x.DeletedAtUtc == null &&
                x.ScanLeaseToken == leaseToken &&
                x.ScanLeaseExpiresAtUtc > failedAtUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ScanFailureCode, failureCode)
                .SetProperty(x => x.NextScanAttemptAtUtc, nextAttemptAtUtc)
                .SetProperty(x => x.ScanLeaseToken, (Guid?)null)
                .SetProperty(x => x.ScanLeaseExpiresAtUtc, (DateTime?)null));
        return updated == 1;
    }

    public Task<bool> IsStorageKeyReferencedAsync(Guid storageKey)
    {
        return context.RequestAttachments
            .AnyAsync(x => x.StorageKey == storageKey);
    }

    public async Task AddRangeAsync(
        IEnumerable<RequestAttachment> attachments)
    {
        await context.RequestAttachments.AddRangeAsync(attachments);
    }

    public async Task<AttachmentSaveResult> TryAddWithinLimitsAsync(
        int requestId,
        IEnumerable<RequestAttachment> attachments,
        int maximumFiles,
        long maximumTotalBytes)
    {
        var newAttachments = attachments.ToList();
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var isPending = await context.ApprovalRequests.AnyAsync(x =>
            x.Id == requestId &&
            x.Status == RequestStatus.Pending);
        if (!isPending)
        {
            return AttachmentSaveResult.RequestNoLongerPending;
        }

        var query = context.RequestAttachments
            .Where(x => x.RequestId == requestId && x.DeletedAtUtc == null);
        var currentCount = await query.CountAsync();
        if (currentCount + newAttachments.Count > maximumFiles)
        {
            return AttachmentSaveResult.FileCountLimitExceeded;
        }

        var currentTotal = await query.SumAsync(x => (long?)x.SizeBytes) ?? 0;
        if (newAttachments.Sum(x => x.SizeBytes) + currentTotal >
            maximumTotalBytes)
        {
            return AttachmentSaveResult.TotalSizeLimitExceeded;
        }

        await context.RequestAttachments.AddRangeAsync(newAttachments);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return AttachmentSaveResult.Added;
    }

    public async Task<bool> TrySoftDeleteOwnedPendingAsync(
        int requestId,
        long attachmentId,
        int ownerUserId,
        DateTime deletedAtUtc)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var canRemove = await context.ApprovalRequests.AnyAsync(x =>
            x.Id == requestId &&
            x.CreatedByUserId == ownerUserId &&
            x.Status == RequestStatus.Pending);
        if (!canRemove)
        {
            return false;
        }

        var attachment = await context.RequestAttachments
            .FirstOrDefaultAsync(x =>
                x.RequestId == requestId &&
                x.Id == attachmentId &&
                x.DeletedAtUtc == null);
        if (attachment is null)
        {
            return false;
        }

        attachment.SoftDelete(ownerUserId, deletedAtUtc);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    public Task SaveChangesAsync()
    {
        return context.SaveChangesAsync();
    }
}
