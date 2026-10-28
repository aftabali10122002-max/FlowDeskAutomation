using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.DTOs;

public sealed record PreparedRequestAttachment(
    Guid StorageKey,
    Guid ClientUploadId,
    string OriginalFileName,
    string Extension,
    string MediaType,
    long SizeBytes,
    string Sha256,
    AttachmentScanStatus ScanStatus,
    string? ScannerName,
    DateTime? ScannedAtUtc);
