using FlowDesk.Application.Interfaces;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class LocalPrivateFileStorageTests
{
    [Fact]
    public void Constructor_RejectsStoragePathInsideWebRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(root, "wwwroot");
        Directory.CreateDirectory(webRoot);
        try
        {
            var configuration = CreateConfiguration(
                Path.Combine(root, "profile"),
                Path.Combine(webRoot, "attachments"));

            Assert.Throws<InvalidOperationException>(() =>
                new LocalPrivateFileStorage(
                    root,
                    webRoot,
                    configuration));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_RejectsStorageRootRedirectedIntoWebRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(root, "wwwroot");
        var outside = Path.Combine(root, "private");
        var inWebRoot = Path.Combine(webRoot, "attachments");
        var attachmentLink = Path.Combine(outside, "attachments");
        Directory.CreateDirectory(inWebRoot);
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(attachmentLink);
        try
        {
            var storage = new LocalPrivateFileStorage(
                root,
                webRoot,
                CreateConfiguration(
                    Path.Combine(outside, "profiles"),
                    attachmentLink));
            Directory.Delete(attachmentLink);
            try
            {
                Directory.CreateSymbolicLink(attachmentLink, inWebRoot);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or
                PlatformNotSupportedException or
                IOException)
            {
                return;
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                storage.SaveAsync(
                    PrivateFileArea.RequestAttachments,
                    Guid.NewGuid(),
                    "pdf",
                    new MemoryStream([1, 2, 3]),
                    CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GetEntriesAsync_ListsOnlyPrivateAttachmentObjects()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(root, "wwwroot");
        var attachmentRoot = Path.Combine(root, "attachments");
        Directory.CreateDirectory(webRoot);
        var storage = new LocalPrivateFileStorage(
            root,
            webRoot,
            CreateConfiguration(
                Path.Combine(root, "profiles"),
                attachmentRoot));
        var key = Guid.NewGuid();
        await storage.SaveAsync(
            PrivateFileArea.RequestAttachments,
            key,
            "pdf",
            new MemoryStream([1, 2, 3]),
            CancellationToken.None);
        var temporaryUploadPath = Path.Combine(
            attachmentRoot,
            $"{key:N}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(temporaryUploadPath, [4, 5, 6]);
        File.SetLastWriteTimeUtc(
            temporaryUploadPath,
            DateTime.UtcNow.AddDays(-2));

        try
        {
            var entries = await storage.GetEntriesAsync(
                PrivateFileArea.RequestAttachments,
                continuationToken: null,
                maximumEntries: 10,
                CancellationToken.None);

            var entry = Assert.Single(entries.Entries);
            Assert.Equal(key, entry.StorageKey);
            Assert.Equal("pdf", entry.Extension);
            Assert.Equal(DateTimeKind.Utc, entry.LastModifiedAtUtc.Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GetEntriesAsync_UsesStableBoundedPages()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(root, "wwwroot");
        var attachmentRoot = Path.Combine(root, "attachments");
        Directory.CreateDirectory(webRoot);
        var storage = new LocalPrivateFileStorage(
            root,
            webRoot,
            CreateConfiguration(
                Path.Combine(root, "profiles"),
                attachmentRoot));
        var keys = new[]
        {
            Guid.ParseExact("11111111111111111111111111111111", "N"),
            Guid.ParseExact("22222222222222222222222222222222", "N"),
            Guid.ParseExact("33333333333333333333333333333333", "N")
        };

        try
        {
            foreach (var key in keys)
            {
                await storage.SaveAsync(
                    PrivateFileArea.RequestAttachments,
                    key,
                    "pdf",
                    new MemoryStream([1, 2, 3]),
                    CancellationToken.None);
            }

            var first = await storage.GetEntriesAsync(
                PrivateFileArea.RequestAttachments,
                continuationToken: null,
                maximumEntries: 1,
                CancellationToken.None);
            var second = await storage.GetEntriesAsync(
                PrivateFileArea.RequestAttachments,
                first.ContinuationToken,
                maximumEntries: 1,
                CancellationToken.None);
            var third = await storage.GetEntriesAsync(
                PrivateFileArea.RequestAttachments,
                second.ContinuationToken,
                maximumEntries: 1,
                CancellationToken.None);

            Assert.Equal(keys[0], Assert.Single(first.Entries).StorageKey);
            Assert.Equal(keys[1], Assert.Single(second.Entries).StorageKey);
            Assert.Equal(keys[2], Assert.Single(third.Entries).StorageKey);
            Assert.Null(third.ContinuationToken);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IConfiguration CreateConfiguration(
        string profilePath,
        string attachmentPath)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProfileImages:StoragePath"] = profilePath,
                ["Attachments:StoragePath"] = attachmentPath
            })
            .Build();
    }
}
