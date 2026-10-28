using FlowDesk.Application.Interfaces;
using SkiaSharp;

namespace FlowDesk.Infrastructure.Persistence;

public sealed class LocalProfileImageStorage : IProfileImageStorage
{
    public const int MaximumImageSizeBytes = 2 * 1024 * 1024;
    private const int MaximumImageDimension = 4096;
    private const long MaximumImagePixels = 16_000_000;
    private static readonly HashSet<SKEncodedImageFormat> AllowedFormats =
        [SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp];

    private readonly IPrivateFileStorage fileStorage;

    public LocalProfileImageStorage(IPrivateFileStorage fileStorage)
    {
        this.fileStorage = fileStorage;
    }

    public async Task<string> SaveAsync(
        Stream image,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var input = await ReadBoundedAsync(image, cancellationToken);
        input.Position = 0;

        using var codec = SKCodec.Create(input);
        if (codec is null || !AllowedFormats.Contains(codec.EncodedFormat))
        {
            throw new ProfileImageValidationException(
                "Upload a valid JPEG, PNG, or WebP image.");
        }

        var imageInfo = codec.Info;
        if (imageInfo.Width > MaximumImageDimension ||
            imageInfo.Height > MaximumImageDimension ||
            (long)imageInfo.Width * imageInfo.Height > MaximumImagePixels)
        {
            throw new ProfileImageValidationException(
                "The image dimensions exceed the allowed limit.");
        }

        using var decodedImage = SKBitmap.Decode(codec);
        if (decodedImage is null)
        {
            throw new ProfileImageValidationException(
                "The uploaded image is invalid or corrupted.");
        }

        using var encodedImage = SKImage.FromBitmap(decodedImage)
            .Encode(SKEncodedImageFormat.Jpeg, 90);
        if (encodedImage is null)
        {
            throw new InvalidOperationException(
                "The uploaded profile image could not be encoded.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var storageKey = Guid.NewGuid();
        await using var encodedStream = new MemoryStream();
        encodedImage.SaveTo(encodedStream);
        encodedStream.Position = 0;
        await fileStorage.SaveAsync(
            PrivateFileArea.ProfileImages,
            storageKey,
            "jpg",
            encodedStream,
            cancellationToken);
        return storageKey.ToString("N");
    }

    public Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Guid.TryParseExact(storageKey, "N", out var key))
        {
            throw new InvalidOperationException(
                "Stored profile image key is invalid.");
        }

        return fileStorage.OpenReadAsync(
            PrivateFileArea.ProfileImages,
            key,
            "jpg",
            cancellationToken);
    }

    public Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Guid.TryParseExact(storageKey, "N", out var key))
        {
            throw new InvalidOperationException(
                "Stored profile image key is invalid.");
        }

        return fileStorage.DeleteAsync(
            PrivateFileArea.ProfileImages,
            key,
            "jpg",
            cancellationToken);
    }

    private static async Task<MemoryStream> ReadBoundedAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var output = new MemoryStream();
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                output.Position = 0;
                return output;
            }

            if (output.Length + read > MaximumImageSizeBytes)
            {
                output.Dispose();
                throw new ProfileImageTooLargeException(
                    "Profile images cannot exceed 2 MB.");
            }

            await output.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }
    }

}

public sealed class ProfileImageValidationException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);

public sealed class ProfileImageTooLargeException(string message)
    : Exception(message);
