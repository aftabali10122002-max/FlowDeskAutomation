using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public sealed class RequestAttachment
{
    public long Id { get; private set; }

    public int RequestId { get; private set; }

    public ApprovalRequest Request { get; private set; } = null!;

    public int UploadedByUserId { get; private set; }

    public User UploadedByUser { get; private set; } = null!;

    public Guid StorageKey { get; private set; }

    public Guid ClientUploadId { get; private set; }

    public string OriginalFileName { get; private set; } = string.Empty;

    public string Extension { get; private set; } = string.Empty;

    public string MediaType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; } = string.Empty;

    public DateTime UploadedAtUtc { get; private set; }

    public AttachmentScanStatus ScanStatus { get; private set; }

    public DateTime? ScannedAtUtc { get; private set; }

    public string? ScannerName { get; private set; }

    public int ScanAttempts { get; private set; }

    public DateTime? LastScanAttemptAtUtc { get; private set; }

    public DateTime? NextScanAttemptAtUtc { get; private set; }

    public Guid? ScanLeaseToken { get; private set; }

    public DateTime? ScanLeaseExpiresAtUtc { get; private set; }

    public string? ScanFailureCode { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    public int? DeletedByUserId { get; private set; }

    public bool StorageCleanupPending { get; private set; }

    private RequestAttachment()
    {
    }

    public RequestAttachment(
        int requestId,
        int uploadedByUserId,
        Guid storageKey,
        Guid clientUploadId,
        string originalFileName,
        string extension,
        string mediaType,
        long sizeBytes,
        string sha256,
        DateTime uploadedAtUtc)
    {
        if (requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        RequestId = requestId;
        Initialize(
            uploadedByUserId,
            storageKey,
            clientUploadId,
            originalFileName,
            extension,
            mediaType,
            sizeBytes,
            sha256,
            uploadedAtUtc);
    }

    public RequestAttachment(
        ApprovalRequest request,
        int uploadedByUserId,
        Guid storageKey,
        Guid clientUploadId,
        string originalFileName,
        string extension,
        string mediaType,
        long sizeBytes,
        string sha256,
        DateTime uploadedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
        RequestId = request.Id;
        Initialize(
            uploadedByUserId,
            storageKey,
            clientUploadId,
            originalFileName,
            extension,
            mediaType,
            sizeBytes,
            sha256,
            uploadedAtUtc);
    }

    public void MarkScanned(
        bool isClean,
        string scannerName,
        DateTime scannedAtUtc)
    {
        if (ScanStatus is not AttachmentScanStatus.Pending and
            not AttachmentScanStatus.NotScanned)
        {
            throw new InvalidOperationException(
                "Only pending attachments can be scanned.");
        }

        if (string.IsNullOrWhiteSpace(scannerName) || scannerName.Length > 100)
        {
            throw new ArgumentException(
                "A scanner name of at most 100 characters is required.",
                nameof(scannerName));
        }

        EnsureUtc(scannedAtUtc, nameof(scannedAtUtc));
        ScanStatus = isClean
            ? AttachmentScanStatus.Clean
            : AttachmentScanStatus.Rejected;
        ScannerName = scannerName.Trim();
        ScannedAtUtc = scannedAtUtc;
    }

    public void MarkNotScanned(DateTime completedAtUtc)
    {
        if (ScanStatus != AttachmentScanStatus.Pending)
        {
            throw new InvalidOperationException(
                "Only pending attachments can be marked not scanned.");
        }

        EnsureUtc(completedAtUtc, nameof(completedAtUtc));
        ScanStatus = AttachmentScanStatus.NotScanned;
        ScannedAtUtc = completedAtUtc;
    }

    public void ClaimScan(
        Guid leaseToken,
        DateTime claimedAtUtc,
        DateTime leaseExpiresAtUtc)
    {
        if (leaseToken == Guid.Empty)
        {
            throw new ArgumentException(
                "A scan lease token is required.",
                nameof(leaseToken));
        }

        if (ScanStatus is not AttachmentScanStatus.Pending and
                not AttachmentScanStatus.NotScanned ||
            DeletedAtUtc.HasValue ||
            (ScanLeaseExpiresAtUtc.HasValue &&
             ScanLeaseExpiresAtUtc > claimedAtUtc) ||
            (NextScanAttemptAtUtc.HasValue &&
             NextScanAttemptAtUtc > claimedAtUtc))
        {
            throw new InvalidOperationException(
                "The attachment is not available for scanning.");
        }

        EnsureUtc(claimedAtUtc, nameof(claimedAtUtc));
        EnsureUtc(leaseExpiresAtUtc, nameof(leaseExpiresAtUtc));
        if (leaseExpiresAtUtc <= claimedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseExpiresAtUtc));
        }

        ScanAttempts = checked(ScanAttempts + 1);
        ScanStatus = AttachmentScanStatus.Pending;
        ScannerName = null;
        ScannedAtUtc = null;
        LastScanAttemptAtUtc = claimedAtUtc;
        NextScanAttemptAtUtc = null;
        ScanLeaseToken = leaseToken;
        ScanLeaseExpiresAtUtc = leaseExpiresAtUtc;
        ScanFailureCode = null;
    }

    public void CompleteScan(
        Guid leaseToken,
        AttachmentScanStatus status,
        string? scannerName,
        DateTime scannedAtUtc)
    {
        EnsureCurrentScanLease(leaseToken);
        if (status is not AttachmentScanStatus.Clean and
            not AttachmentScanStatus.Rejected)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (string.IsNullOrWhiteSpace(scannerName))
        {
            throw new ArgumentException(
                "A scan result requires a scanner name.",
                nameof(scannerName));
        }

        if (!string.IsNullOrWhiteSpace(scannerName) &&
            scannerName.Length > 100)
        {
            throw new ArgumentException(
                "The scanner name cannot exceed 100 characters.",
                nameof(scannerName));
        }

        EnsureUtc(scannedAtUtc, nameof(scannedAtUtc));
        ScanStatus = status;
        ScannerName = scannerName?.Trim();
        ScannedAtUtc = scannedAtUtc;
        ScanFailureCode = status == AttachmentScanStatus.Rejected
            ? "malware_detected"
            : null;
        ClearScanLease();
    }

    public void FailScan(
        Guid leaseToken,
        string failureCode,
        DateTime failedAtUtc,
        DateTime? nextAttemptAtUtc)
    {
        EnsureCurrentScanLease(leaseToken);
        if (!IsSafeScanFailureCode(failureCode))
        {
            throw new ArgumentException(
                "The scan failure code is invalid.",
                nameof(failureCode));
        }

        EnsureUtc(failedAtUtc, nameof(failedAtUtc));
        if (nextAttemptAtUtc.HasValue)
        {
            EnsureUtc(nextAttemptAtUtc.Value, nameof(nextAttemptAtUtc));
            if (nextAttemptAtUtc <= failedAtUtc)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nextAttemptAtUtc));
            }
        }

        ScanFailureCode = failureCode;
        NextScanAttemptAtUtc = nextAttemptAtUtc;
        ClearScanLease();
    }

    public void SoftDelete(int deletedByUserId, DateTime deletedAtUtc)
    {
        if (deletedByUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deletedByUserId));
        }

        if (DeletedAtUtc.HasValue)
        {
            throw new InvalidOperationException(
                "The attachment has already been removed.");
        }

        EnsureUtc(deletedAtUtc, nameof(deletedAtUtc));
        DeletedByUserId = deletedByUserId;
        DeletedAtUtc = deletedAtUtc;
        StorageCleanupPending = true;
    }

    public void MarkStorageCleanupComplete()
    {
        if (!DeletedAtUtc.HasValue)
        {
            throw new InvalidOperationException(
                "Only removed attachments can be cleaned up.");
        }

        StorageCleanupPending = false;
    }

    private void Initialize(
        int uploadedByUserId,
        Guid storageKey,
        Guid clientUploadId,
        string originalFileName,
        string extension,
        string mediaType,
        long sizeBytes,
        string sha256,
        DateTime uploadedAtUtc)
    {
        if (uploadedByUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(uploadedByUserId));
        }

        if (storageKey == Guid.Empty || clientUploadId == Guid.Empty)
        {
            throw new ArgumentException("Storage and upload keys are required.");
        }

        if (string.IsNullOrWhiteSpace(originalFileName) ||
            originalFileName.Length > 255)
        {
            throw new ArgumentException(
                "A file name of at most 255 characters is required.",
                nameof(originalFileName));
        }

        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 10)
        {
            throw new ArgumentException(
                "A valid file extension is required.",
                nameof(extension));
        }

        if (string.IsNullOrWhiteSpace(mediaType) || mediaType.Length > 100)
        {
            throw new ArgumentException(
                "A valid media type is required.",
                nameof(mediaType));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        }

        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "A SHA-256 hash is required.",
                nameof(sha256));
        }

        EnsureUtc(uploadedAtUtc, nameof(uploadedAtUtc));
        UploadedByUserId = uploadedByUserId;
        StorageKey = storageKey;
        ClientUploadId = clientUploadId;
        OriginalFileName = originalFileName;
        Extension = extension;
        MediaType = mediaType;
        SizeBytes = sizeBytes;
        Sha256 = sha256.ToUpperInvariant();
        UploadedAtUtc = uploadedAtUtc;
        ScanStatus = AttachmentScanStatus.Pending;
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Timestamp must be UTC.",
                parameterName);
        }
    }

    private void EnsureCurrentScanLease(Guid leaseToken)
    {
        if (leaseToken == Guid.Empty ||
            ScanStatus != AttachmentScanStatus.Pending ||
            DeletedAtUtc.HasValue ||
            ScanLeaseToken != leaseToken)
        {
            throw new InvalidOperationException(
                "The scan lease is no longer current.");
        }
    }

    private void ClearScanLease()
    {
        ScanLeaseToken = null;
        ScanLeaseExpiresAtUtc = null;
    }

    private static bool IsSafeScanFailureCode(string value)
    {
        return value is "scanner_unavailable" or
            "scan_timeout" or
            "invalid_scanner_response" or
            "hash_mismatch" or
            "storage_content_missing" or
            "scan_failed" or
            "retry_exhausted";
    }
}
