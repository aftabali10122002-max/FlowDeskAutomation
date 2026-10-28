namespace FlowDesk.Application.DTOs;

public sealed record RequestAttachmentScanWorkItem(
    long AttachmentId,
    Guid StorageKey,
    string Extension,
    string MediaType,
    string Sha256,
    Guid LeaseToken,
    int ScanAttempts);
