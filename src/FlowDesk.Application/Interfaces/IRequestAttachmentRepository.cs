using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Application.DTOs;

namespace FlowDesk.Application.Interfaces;

public interface IRequestAttachmentRepository
{
    Task<List<RequestAttachment>> GetForRequestAsync(int requestId);

    Task<RequestAttachment?> GetForRequestByIdAsync(
        int requestId,
        long attachmentId,
        bool includeDeleted = false);

    Task<RequestAttachment?> GetByClientUploadIdAsync(Guid clientUploadId);

    Task<List<RequestAttachment>> GetPendingStorageCleanupAsync(
        int batchSize,
        DateTime nowUtc);

    Task<IReadOnlyList<RequestAttachmentScanWorkItem>> ClaimPendingScansAsync(
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc,
        int maximumAttempts,
        int batchSize);

    Task<bool> CompleteScanAsync(
        long attachmentId,
        Guid leaseToken,
        AttachmentScanStatus status,
        string? scannerName,
        DateTime scannedAtUtc);

    Task<bool> FailScanAsync(
        long attachmentId,
        Guid leaseToken,
        string failureCode,
        DateTime failedAtUtc,
        DateTime? nextAttemptAtUtc);

    Task<bool> IsStorageKeyReferencedAsync(Guid storageKey);

    Task AddRangeAsync(IEnumerable<RequestAttachment> attachments);

    Task<AttachmentSaveResult> TryAddWithinLimitsAsync(
        int requestId,
        IEnumerable<RequestAttachment> attachments,
        int maximumFiles,
        long maximumTotalBytes);

    Task<bool> TrySoftDeleteOwnedPendingAsync(
        int requestId,
        long attachmentId,
        int ownerUserId,
        DateTime deletedAtUtc);

    Task SaveChangesAsync();
}

public enum AttachmentSaveResult
{
    Added,
    RequestNoLongerPending,
    FileCountLimitExceeded,
    TotalSizeLimitExceeded
}
