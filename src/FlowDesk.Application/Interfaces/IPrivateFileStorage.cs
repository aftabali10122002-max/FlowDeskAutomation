namespace FlowDesk.Application.Interfaces;

public interface IPrivateFileStorage
{
    Task SaveAsync(
        PrivateFileArea area,
        Guid storageKey,
        string extension,
        Stream content,
        CancellationToken cancellationToken);

    Task<Stream?> OpenReadAsync(
        PrivateFileArea area,
        Guid storageKey,
        string extension,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        PrivateFileArea area,
        Guid storageKey,
        string extension,
        CancellationToken cancellationToken);

    Task<PrivateFileStoragePage> GetEntriesAsync(
        PrivateFileArea area,
        string? continuationToken,
        int maximumEntries,
        CancellationToken cancellationToken);
}
