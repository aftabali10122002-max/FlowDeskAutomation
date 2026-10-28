using System.Security.Cryptography;
using System.Text;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Enums;
using FlowDisk.Api.Attachments;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class AttachmentIntakeServiceTests
{
    [Fact]
    public void Options_RejectsUnscannedDownloadConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Attachments:AllowUnscannedDownloads"] = "true"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            RequestAttachmentOptions.FromConfiguration(configuration));
    }

    [Fact]
    public async Task StoreFilesAsync_ScansTheStoredBytesAndVerifiesTheirHash()
    {
        var storage = new MemoryPrivateFileStorage();
        var service = CreateService(storage);
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\nfile content");
        var validated = await service.ValidateFilesAsync(
            [CreateFile(bytes)],
            [Guid.NewGuid()],
            CancellationToken.None);
        using var validatedFiles = new DisposableAttachments(validated);

        var stored = await service.StoreFilesAsync(
            validated,
            CancellationToken.None);

        var saved = Assert.Single(stored);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)),
            saved.Sha256);
        Assert.Equal(AttachmentScanStatus.Pending, saved.ScanStatus);
        Assert.Equal(bytes, storage.Files[saved.StorageKey]);
    }

    [Fact]
    public async Task CleanupStoredFilesAsync_RetriesTransientStorageFailures()
    {
        var storage = new MemoryPrivateFileStorage
        {
            FailDeleteCount = 2
        };
        var service = CreateService(storage);
        var prepared = new FlowDesk.Application.DTOs.PreparedRequestAttachment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "receipt.pdf",
            "pdf",
            "application/pdf",
            4,
            new string('A', 64),
            AttachmentScanStatus.NotScanned,
            null,
            DateTime.UtcNow);

        await service.CleanupStoredFilesAsync(
            [prepared],
            CancellationToken.None);

        Assert.Equal(3, storage.DeleteCalls);
    }

    [Fact]
    public async Task CleanupStoredFilesAsync_ReportsPersistentStorageFailures()
    {
        var storage = new MemoryPrivateFileStorage
        {
            FailDeleteCount = 3
        };
        var service = CreateService(storage);
        var prepared = new FlowDesk.Application.DTOs.PreparedRequestAttachment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "receipt.pdf",
            "pdf",
            "application/pdf",
            4,
            new string('A', 64),
            AttachmentScanStatus.NotScanned,
            null,
            DateTime.UtcNow);
        storage.Files[prepared.StorageKey] = [1];

        await Assert.ThrowsAsync<AggregateException>(() =>
            service.CleanupStoredFilesAsync(
                [prepared],
                CancellationToken.None));

        Assert.Equal(3, storage.DeleteCalls);
        Assert.Contains(prepared.StorageKey, storage.Files.Keys);
    }

    private static AttachmentIntakeService CreateService(
        IPrivateFileStorage storage)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new AttachmentIntakeService(
            RequestAttachmentOptions.FromConfiguration(configuration),
            storage,
            NullLogger<AttachmentIntakeService>.Instance);
    }

    private static IFormFile CreateFile(byte[] bytes)
    {
        return new FormFile(
            new MemoryStream(bytes),
            0,
            bytes.Length,
            "file",
            "receipt.pdf");
    }

    private sealed class DisposableAttachments(
        IReadOnlyList<ValidatedAttachment> attachments) : IDisposable
    {
        public void Dispose()
        {
            foreach (var attachment in attachments)
            {
                attachment.Dispose();
            }
        }
    }

    private sealed class MemoryPrivateFileStorage : IPrivateFileStorage
    {
        public Dictionary<Guid, byte[]> Files { get; } = [];
        public int DeleteCalls { get; private set; }
        public int FailDeleteCount { get; init; }

        public async Task SaveAsync(
            PrivateFileArea area,
            Guid storageKey,
            string extension,
            Stream content,
            CancellationToken cancellationToken)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            Files[storageKey] = copy.ToArray();
        }

        public Task<Stream?> OpenReadAsync(
            PrivateFileArea area,
            Guid storageKey,
            string extension,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Stream?>(
                Files.TryGetValue(storageKey, out var bytes)
                    ? new MemoryStream(bytes, writable: false)
                    : null);
        }

        public Task DeleteAsync(
            PrivateFileArea area,
            Guid storageKey,
            string extension,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteCalls++;
            if (DeleteCalls <= FailDeleteCount)
            {
                throw new IOException("Synthetic storage failure.");
            }

            Files.Remove(storageKey);
            return Task.CompletedTask;
        }

        public Task<PrivateFileStoragePage> GetEntriesAsync(
            PrivateFileArea area,
            string? continuationToken,
            int maximumEntries,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = Files.Keys
                    .Take(maximumEntries)
                    .Select(key => new PrivateFileStorageEntry(
                        key,
                        "pdf",
                        DateTime.UtcNow.AddDays(-2)))
                    .ToList();
            return Task.FromResult(new PrivateFileStoragePage(entries, null));
        }
    }
}
