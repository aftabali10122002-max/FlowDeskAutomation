namespace FlowDesk.Application.Interfaces;

public interface IProfileImageStorage
{
    Task<string> SaveAsync(Stream image, CancellationToken cancellationToken);

    Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
