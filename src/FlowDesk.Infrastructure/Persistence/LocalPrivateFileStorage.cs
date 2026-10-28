using System.Text.RegularExpressions;
using FlowDesk.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FlowDesk.Infrastructure.Persistence;

public sealed class LocalPrivateFileStorage : IPrivateFileStorage
{
    private static readonly HashSet<string> AttachmentExtensions =
        ["pdf", "jpg", "jpeg", "png", "docx", "xlsx"];
    private static readonly Regex SafeExtension = new(
        "^[a-z0-9]{1,10}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly string profileImageRoot;
    private readonly string attachmentRoot;
    private readonly string? webRootPath;

    public LocalPrivateFileStorage(
        string contentRootPath,
        string? webRootPath,
        IConfiguration configuration)
    {
        this.webRootPath = string.IsNullOrWhiteSpace(webRootPath)
            ? null
            : Path.GetFullPath(webRootPath);
        profileImageRoot = ResolvePath(
            contentRootPath,
            configuration["ProfileImages:StoragePath"],
            Path.Combine("App_Data", "ProfileImages"));
        attachmentRoot = ResolvePath(
            contentRootPath,
            configuration["Attachments:StoragePath"],
            Path.Combine("App_Data", "RequestAttachments"));

        EnsureOutsideWebRoot(webRootPath, profileImageRoot);
        EnsureOutsideWebRoot(webRootPath, attachmentRoot);
        if (string.Equals(
            profileImageRoot,
            attachmentRoot,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Profile images and request attachments must use separate storage directories.");
        }
    }

    public async Task SaveAsync(
        PrivateFileArea area,
        Guid storageKey,
        string extension,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var filePath = GetFilePath(area, storageKey, extension);
        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        EnsureOutsideWebRoot(webRootPath, GetRoot(area));
        Directory.CreateDirectory(GetRoot(area));
        EnsureOutsideWebRoot(webRootPath, GetRoot(area));
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, filePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<Stream?> OpenReadAsync(
        PrivateFileArea area,
        Guid storageKey,
        string extension,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = GetFilePath(area, storageKey, extension);
        if (!File.Exists(filePath))
        {
            return Task.FromResult<Stream?>(null);
        }

        EnsureNotLink(filePath);
        Stream stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<PrivateFileStoragePage> GetEntriesAsync(
        PrivateFileArea area,
        string? continuationToken,
        int maximumEntries,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        var root = GetRoot(area);
        EnsureOutsideWebRoot(webRootPath, root);
        if (!Directory.Exists(root))
        {
            return Task.FromResult(new PrivateFileStoragePage([], null));
        }

        var nextFiles = new SortedSet<string>(StringComparer.Ordinal);
        var entryByFileName = new Dictionary<string, PrivateFileStorageEntry>(
            StringComparer.Ordinal);
        var hasMore = false;
        foreach (var filePath in Directory.EnumerateFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(filePath);
            if (continuationToken is not null &&
                string.CompareOrdinal(fileName, continuationToken) <= 0)
            {
                continue;
            }

            var extension = Path.GetExtension(fileName).TrimStart('.')
                .ToLowerInvariant();
            var keyText = Path.GetFileNameWithoutExtension(fileName);
            if (!Guid.TryParseExact(keyText, "N", out var storageKey) ||
                storageKey == Guid.Empty ||
                !IsAllowedExtension(area, extension))
            {
                continue;
            }

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(filePath);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            DateTime lastModified;
            try
            {
                lastModified = File.GetLastWriteTimeUtc(filePath);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            var entry = new PrivateFileStorageEntry(
                storageKey,
                extension,
                DateTime.SpecifyKind(lastModified, DateTimeKind.Utc));
            nextFiles.Add(fileName);
            entryByFileName[fileName] = entry;
            if (nextFiles.Count > maximumEntries)
            {
                hasMore = true;
                var largest = nextFiles.Max!;
                nextFiles.Remove(largest);
                entryByFileName.Remove(largest);
            }
        }

        var fileNames = nextFiles.ToList();
        var entries = fileNames
            .Select(fileName => entryByFileName[fileName])
            .ToList();
        return Task.FromResult(new PrivateFileStoragePage(
            entries,
            hasMore ? fileNames.Last() : null));
    }

    public Task DeleteAsync(
        PrivateFileArea area,
        Guid storageKey,
        string extension,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = GetFilePath(area, storageKey, extension);
        if (File.Exists(filePath))
        {
            EnsureNotLink(filePath);
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    private string GetFilePath(
        PrivateFileArea area,
        Guid storageKey,
        string extension)
    {
        var root = GetRoot(area);
        EnsureOutsideWebRoot(webRootPath, root);
        if (storageKey == Guid.Empty)
        {
            throw new ArgumentException(
                "Storage key is invalid.",
                nameof(storageKey));
        }

        var normalizedExtension = extension.TrimStart('.')
            .ToLowerInvariant();
        if (!IsAllowedExtension(area, normalizedExtension))
        {
            throw new ArgumentException(
                "Storage extension is invalid.",
                nameof(extension));
        }

        return Path.Combine(
            root,
            $"{storageKey:N}.{normalizedExtension}");
    }

    private static bool IsAllowedExtension(
        PrivateFileArea area,
        string extension)
    {
        return SafeExtension.IsMatch(extension) &&
            (area switch
            {
                PrivateFileArea.ProfileImages => extension == "jpg",
                PrivateFileArea.RequestAttachments =>
                    AttachmentExtensions.Contains(extension),
                _ => false
            });
    }

    private static void EnsureNotLink(string filePath)
    {
        if ((File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "Private file storage cannot access a linked file.");
        }
    }

    private string GetRoot(PrivateFileArea area)
    {
        return area switch
        {
            PrivateFileArea.ProfileImages => profileImageRoot,
            PrivateFileArea.RequestAttachments => attachmentRoot,
            _ => throw new ArgumentOutOfRangeException(nameof(area))
        };
    }

    private static string ResolvePath(
        string contentRootPath,
        string? configuredPath,
        string defaultRelativePath)
    {
        return Path.GetFullPath(
            string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(contentRootPath, defaultRelativePath)
                : Path.IsPathRooted(configuredPath)
                    ? configuredPath
                    : Path.Combine(contentRootPath, configuredPath));
    }

    private static void EnsureOutsideWebRoot(
        string? webRoot,
        string storagePath)
    {
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            return;
        }

        EnsurePathOutside(
            Path.GetFullPath(webRoot),
            Path.GetFullPath(storagePath));
        EnsurePathOutside(
            ResolvePhysicalPath(webRoot),
            ResolvePhysicalPath(storagePath));
    }

    private static void EnsurePathOutside(
        string webRoot,
        string storagePath)
    {
        var relativePath = Path.GetRelativePath(webRoot, storagePath);
        var isOutside = relativePath == ".." ||
            relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            relativePath.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath);
        if (!isOutside)
        {
            throw new InvalidOperationException(
                "Private file storage must be outside the web root.");
        }
    }

    private static string ResolvePhysicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var current = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException(
                "The configured storage path is invalid.");
        var components = fullPath[current.Length..]
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

        foreach (var component in components)
        {
            var candidate = Path.Combine(current, component);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(candidate);
            }
            catch (FileNotFoundException)
            {
                current = candidate;
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                current = candidate;
                continue;
            }

            var fileSystemInfo = (attributes & FileAttributes.Directory) != 0
                ? (FileSystemInfo)new DirectoryInfo(candidate)
                : new FileInfo(candidate);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = fileSystemInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (target is null)
                {
                    throw new InvalidOperationException(
                        "Private file storage cannot use unresolved filesystem links.");
                }

                current = Path.GetFullPath(target.FullName);
            }
            else
            {
                current = candidate;
            }
        }

        return Path.GetFullPath(current);
    }
}
