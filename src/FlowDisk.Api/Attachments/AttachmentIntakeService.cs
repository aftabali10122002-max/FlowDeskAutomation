using System.IO.Compression;
using System.Security.Cryptography;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Enums;

namespace FlowDisk.Api.Attachments;

public sealed class AttachmentIntakeService(
    RequestAttachmentOptions options,
    IPrivateFileStorage storage,
    ILogger<AttachmentIntakeService> logger)
{
    private static readonly IReadOnlyDictionary<string, string> MediaTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".docx"] =
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xlsx"] =
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

    public async Task<IReadOnlyList<ValidatedAttachment>> ValidateFilesAsync(
        IReadOnlyList<IFormFile> files,
        IReadOnlyList<Guid> clientUploadIds,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0 || files.Count > options.MaximumFilesPerRequest)
        {
            throw new AttachmentValidationException(
                $"Select between 1 and {options.MaximumFilesPerRequest} files.");
        }

        if (clientUploadIds.Count != files.Count ||
            clientUploadIds.Any(x => x == Guid.Empty) ||
            clientUploadIds.Distinct().Count() != clientUploadIds.Count)
        {
            throw new AttachmentValidationException(
                "Each file requires a unique upload identifier.");
        }

        var validated = new List<ValidatedAttachment>(files.Count);
        long totalSize = 0;
        try
        {
            for (var index = 0; index < files.Count; index++)
            {
                var attachment = await ValidateFileAsync(
                    files[index],
                    clientUploadIds[index],
                    cancellationToken);
                totalSize = checked(totalSize + attachment.SizeBytes);
                if (totalSize > options.MaximumTotalSizeBytes)
                {
                    attachment.Dispose();
                    throw new AttachmentTooLargeException(
                        $"Attachments cannot exceed {options.MaximumTotalSizeBytes / (1024 * 1024)} MB per request.");
                }

                validated.Add(attachment);
            }

            return validated;
        }
        catch
        {
            foreach (var attachment in validated)
            {
                attachment.Dispose();
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<PreparedRequestAttachment>> StoreFilesAsync(
        IReadOnlyList<ValidatedAttachment> files,
        CancellationToken cancellationToken)
    {
        var stored = new List<PreparedRequestAttachment>(files.Count);
        try
        {
            foreach (var file in files)
            {
                var storageKey = Guid.NewGuid();
                file.Content.Position = 0;
                await storage.SaveAsync(
                    PrivateFileArea.RequestAttachments,
                    storageKey,
                    file.Extension,
                    file.Content,
                    cancellationToken);
                stored.Add(new PreparedRequestAttachment(
                    storageKey,
                    file.ClientUploadId,
                    file.OriginalFileName,
                    file.Extension,
                    file.MediaType,
                    file.SizeBytes,
                    file.Sha256,
                    AttachmentScanStatus.Pending,
                    null,
                    null));
                var storedContent = await storage.OpenReadAsync(
                    PrivateFileArea.RequestAttachments,
                    storageKey,
                    file.Extension,
                    cancellationToken);
                if (storedContent is null)
                {
                    throw new AttachmentScannerException(
                        "Stored attachment content could not be verified.");
                }

                await using (storedContent)
                {
                    var storedHash = Convert.ToHexString(
                        await SHA256.HashDataAsync(
                            storedContent,
                            cancellationToken));
                    if (!string.Equals(
                        storedHash,
                        file.Sha256,
                        StringComparison.Ordinal))
                    {
                        throw new AttachmentScannerException(
                            "Stored attachment content did not match its verified hash.");
                    }
                }
            }

            return stored;
        }
        catch (Exception saveException)
        {
            try
            {
                await CleanupStoredFilesAsync(stored, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Attachment storage failed and partial files could not be removed.",
                    saveException,
                    cleanupException);
            }

            throw;
        }
    }

    public async Task CleanupStoredFilesAsync(
        IEnumerable<PreparedRequestAttachment> files,
        CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        foreach (var file in files)
        {
            Exception? lastFailure = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await storage.DeleteAsync(
                        PrivateFileArea.RequestAttachments,
                        file.StorageKey,
                        file.Extension,
                        cancellationToken);
                    lastFailure = null;
                    break;
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException ||
                    !cancellationToken.IsCancellationRequested)
                {
                    lastFailure = exception;
                    if (attempt < 3)
                    {
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(100 * attempt),
                            cancellationToken);
                    }
                }
            }

            if (lastFailure is not null)
            {
                logger.LogError(
                    "Temporary request attachment cleanup failed after retries.");
                (failures ??= []).Add(lastFailure);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more temporary attachment files could not be removed.",
                failures);
        }
    }

    private async Task<ValidatedAttachment> ValidateFileAsync(
        IFormFile file,
        Guid clientUploadId,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
        {
            throw new AttachmentValidationException(
                "Empty files cannot be uploaded.");
        }

        if (file.Length > options.MaximumFileSizeBytes)
        {
            throw new AttachmentTooLargeException(
                $"Each file cannot exceed {options.MaximumFileSizeBytes / (1024 * 1024)} MB.");
        }

        var fileName = SanitizeFileName(file.FileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!MediaTypes.TryGetValue(extension, out var mediaType))
        {
            throw new AttachmentValidationException(
                "Allowed file types are PDF, JPEG, PNG, DOCX, and XLSX.");
        }

        var content = new MemoryStream(
            capacity: checked((int)Math.Min(
                file.Length,
                options.MaximumFileSizeBytes)));
        try
        {
            await using var input = file.OpenReadStream();
            var buffer = new byte[81920];
            long totalRead = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalRead = checked(totalRead + read);
                if (totalRead > options.MaximumFileSizeBytes)
                {
                    throw new AttachmentTooLargeException(
                        $"Each file cannot exceed {options.MaximumFileSizeBytes / (1024 * 1024)} MB.");
                }

                await content.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
            }

            if (totalRead == 0 || totalRead != file.Length)
            {
                throw new AttachmentValidationException(
                    "The uploaded file could not be read completely.");
            }

            content.Position = 0;
            await ValidateSignatureAsync(
                content,
                extension,
                cancellationToken);
            content.Position = 0;
            var hash = Convert.ToHexString(
                await SHA256.HashDataAsync(content, cancellationToken));
            content.Position = 0;
            return new ValidatedAttachment(
                clientUploadId,
                fileName,
                extension.TrimStart('.'),
                mediaType,
                totalRead,
                hash,
                content);
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    private static async Task ValidateSignatureAsync(
        MemoryStream content,
        string extension,
        CancellationToken cancellationToken)
    {
        var prefix = new byte[8];
        var read = await content.ReadAsync(prefix, cancellationToken);
        content.Position = 0;
        var valid = extension switch
        {
            ".pdf" => read >= 5 &&
                prefix.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            ".jpg" or ".jpeg" => read >= 3 &&
                prefix[0] == 0xff &&
                prefix[1] == 0xd8 &&
                prefix[2] == 0xff,
            ".png" => read == 8 &&
                prefix.AsSpan().SequenceEqual(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".docx" or ".xlsx" => read >= 4 &&
                prefix[0] == (byte)'P' &&
                prefix[1] == (byte)'K' &&
                prefix[2] is 3 or 5 or 7 &&
                prefix[3] is 4 or 6 or 8,
            _ => false
        };
        if (!valid)
        {
            throw new AttachmentValidationException(
                "The file contents do not match the selected file type.");
        }

        if (extension is ".docx" or ".xlsx")
        {
            ValidateOfficePackage(content, extension);
        }
    }

    private static void ValidateOfficePackage(
        MemoryStream content,
        string extension)
    {
        try
        {
            using var archive = new ZipArchive(
                content,
                ZipArchiveMode.Read,
                leaveOpen: true);
            if (archive.Entries.Count is 0 or > 500)
            {
                throw new AttachmentValidationException(
                    "The Office document contains an invalid number of entries.");
            }

            long expandedSize = 0;
            var hasContentTypes = false;
            var hasMainDocument = false;
            foreach (var entry in archive.Entries)
            {
                expandedSize = checked(expandedSize + entry.Length);
                if (expandedSize > 50L * 1024 * 1024 ||
                    (entry.Length > 0 &&
                     (entry.CompressedLength == 0 ||
                      entry.Length / Math.Max(1, entry.CompressedLength) > 200)))
                {
                    throw new AttachmentValidationException(
                        "The Office document has unsafe compressed content.");
                }

                var entryName = entry.FullName.Replace('\\', '/');
                if (entryName.Equals(
                        "[Content_Types].xml",
                        StringComparison.OrdinalIgnoreCase))
                {
                    hasContentTypes = true;
                }

                if (extension == ".docx" &&
                    entryName.Equals(
                        "word/document.xml",
                        StringComparison.OrdinalIgnoreCase) ||
                    extension == ".xlsx" &&
                    entryName.Equals(
                        "xl/workbook.xml",
                        StringComparison.OrdinalIgnoreCase))
                {
                    hasMainDocument = true;
                }

                if (entryName.Contains(
                        "vbaproject.bin",
                        StringComparison.OrdinalIgnoreCase) ||
                    entryName.StartsWith(
                        "word/embeddings/",
                        StringComparison.OrdinalIgnoreCase) ||
                    entryName.StartsWith(
                        "xl/embeddings/",
                        StringComparison.OrdinalIgnoreCase) ||
                    entryName.StartsWith(
                        "word/activex/",
                        StringComparison.OrdinalIgnoreCase) ||
                    entryName.StartsWith(
                        "xl/activex/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new AttachmentValidationException(
                        "Macro-enabled or embedded active content is not allowed.");
                }
            }

            if (!hasContentTypes || !hasMainDocument)
            {
                throw new AttachmentValidationException(
                    "The Office document package is invalid.");
            }
        }
        catch (InvalidDataException exception)
        {
            throw new AttachmentValidationException(
                "The Office document package is invalid.",
                exception);
        }
        finally
        {
            content.Position = 0;
        }
    }

    private static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new AttachmentValidationException(
                "A file name is required.");
        }

        var normalized = fileName.Replace('\\', '/');
        var name = normalized[(normalized.LastIndexOf('/') + 1)..]
            .Trim()
            .Trim('.');
        name = string.Concat(name.Where(character =>
            !char.IsControl(character) &&
            character is not '/' and not '\\'));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
        {
            throw new AttachmentValidationException(
                "The file name must be between 1 and 255 characters.");
        }

        return name;
    }
}

public sealed class ValidatedAttachment(
    Guid clientUploadId,
    string originalFileName,
    string extension,
    string mediaType,
    long sizeBytes,
    string sha256,
    MemoryStream content) : IDisposable
{
    public Guid ClientUploadId { get; } = clientUploadId;
    public string OriginalFileName { get; } = originalFileName;
    public string Extension { get; } = extension;
    public string MediaType { get; } = mediaType;
    public long SizeBytes { get; } = sizeBytes;
    public string Sha256 { get; } = sha256;
    public MemoryStream Content { get; } = content;

    public void Dispose()
    {
        Content.Dispose();
    }
}

public sealed class AttachmentValidationException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

public sealed class AttachmentTooLargeException(string message)
    : Exception(message);

public sealed class AttachmentRejectedException(string message)
    : Exception(message);

public sealed class AttachmentScannerException(string message)
    : Exception(message)
{
    public AttachmentScannerException(
        string message,
        string failureCode) : this(message)
    {
        FailureCode = failureCode is "scanner_unavailable" or
            "scan_timeout" or
            "invalid_scanner_response" or
            "hash_mismatch" or
            "storage_content_missing"
            ? failureCode
            : "scan_failed";
    }

    public string FailureCode { get; } = "scan_failed";
}
