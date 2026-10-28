namespace FlowDesk.Application.Interfaces;

public sealed record PrivateFileStorageEntry(
    Guid StorageKey,
    string Extension,
    DateTime LastModifiedAtUtc);

public sealed record PrivateFileStoragePage(
    IReadOnlyList<PrivateFileStorageEntry> Entries,
    string? ContinuationToken);
