using System.Security.Cryptography;
using System.Text;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using FlowDisk.Api.Attachments;
using FlowDisk.Api.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class RequestAttachmentScanWorkerTests
{
    [Fact]
    public async Task ProcessBatchAsync_MarksVerifiedCleanContentClean()
    {
        var scanner = new TestScanner
        {
            Result = new AttachmentScanResult(
                AttachmentScanStatus.Clean,
                "TestScanner")
        };
        using var factory = new ApiFactory
        {
            TestAttachmentMalwareScanner = scanner
        };
        await factory.InitializeDatabaseAsync();
        var attachmentId = await AddPendingAttachmentAsync(factory);

        await CreateWorker(factory, scanner).ProcessBatchAsync(
            CancellationToken.None);

        var attachment = await GetAttachmentAsync(factory, attachmentId);
        Assert.Equal(AttachmentScanStatus.Clean, attachment.ScanStatus);
        Assert.Equal("TestScanner", attachment.ScannerName);
        Assert.Equal(1, attachment.ScanAttempts);
        Assert.Null(attachment.ScanLeaseToken);
        Assert.Null(attachment.ScanFailureCode);
    }

    [Fact]
    public async Task ProcessBatchAsync_MarksInfectedContentRejected()
    {
        var scanner = new TestScanner
        {
            Result = new AttachmentScanResult(
                AttachmentScanStatus.Rejected,
                "TestScanner")
        };
        using var factory = new ApiFactory
        {
            TestAttachmentMalwareScanner = scanner
        };
        await factory.InitializeDatabaseAsync();
        var attachmentId = await AddPendingAttachmentAsync(factory);

        await CreateWorker(factory, scanner).ProcessBatchAsync(
            CancellationToken.None);

        var attachment = await GetAttachmentAsync(factory, attachmentId);
        Assert.Equal(AttachmentScanStatus.Rejected, attachment.ScanStatus);
        Assert.Equal("malware_detected", attachment.ScanFailureCode);
        Assert.Null(attachment.ScanLeaseToken);
    }

    [Fact]
    public async Task ProcessBatchAsync_RechecksPreviouslyUnscannedAttachments()
    {
        var scanner = new TestScanner
        {
            Result = new AttachmentScanResult(
                AttachmentScanStatus.Clean,
                "TestScanner")
        };
        using var factory = new ApiFactory
        {
            TestAttachmentMalwareScanner = scanner
        };
        await factory.InitializeDatabaseAsync();
        var attachmentId = await AddPendingAttachmentAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            var attachment = await context.RequestAttachments
                .SingleAsync(x => x.Id == attachmentId);
            attachment.MarkNotScanned(DateTime.UtcNow);
            await context.SaveChangesAsync();
        }

        await CreateWorker(factory, scanner).ProcessBatchAsync(
            CancellationToken.None);

        var attachmentAfterScan = await GetAttachmentAsync(
            factory,
            attachmentId);
        Assert.Equal(
            AttachmentScanStatus.Clean,
            attachmentAfterScan.ScanStatus);
        Assert.Equal(1, attachmentAfterScan.ScanAttempts);
    }

    [Fact]
    public async Task ProcessBatchAsync_SchedulesFailureAndRetriesAfterBackoff()
    {
        var scanner = new TestScanner();
        scanner.EnqueueFailure("scanner_unavailable");
        scanner.EnqueueResult(new AttachmentScanResult(
            AttachmentScanStatus.Clean,
            "TestScanner"));
        using var factory = new ApiFactory
        {
            AttachmentScanRetryBaseSeconds = 1,
            TestAttachmentMalwareScanner = scanner
        };
        await factory.InitializeDatabaseAsync();
        var attachmentId = await AddPendingAttachmentAsync(factory);
        var worker = CreateWorker(factory, scanner);

        await worker.ProcessBatchAsync(CancellationToken.None);
        var failed = await GetAttachmentAsync(factory, attachmentId);
        Assert.Equal(AttachmentScanStatus.Pending, failed.ScanStatus);
        Assert.Equal("scanner_unavailable", failed.ScanFailureCode);
        Assert.Equal(1, failed.ScanAttempts);
        Assert.True(failed.NextScanAttemptAtUtc > DateTime.UtcNow);
        Assert.Null(failed.ScanLeaseToken);

        await SetNextAttemptToPastAsync(factory, attachmentId);
        await worker.ProcessBatchAsync(CancellationToken.None);

        var retried = await GetAttachmentAsync(factory, attachmentId);
        Assert.Equal(AttachmentScanStatus.Clean, retried.ScanStatus);
        Assert.Equal(2, retried.ScanAttempts);
    }

    [Fact]
    public async Task ProcessBatchAsync_ExhaustsRetriesWithoutGrantingAccess()
    {
        var scanner = new TestScanner();
        scanner.EnqueueFailure("scan_timeout");
        using var factory = new ApiFactory
        {
            AttachmentScanMaximumAttempts = 1,
            TestAttachmentMalwareScanner = scanner
        };
        await factory.InitializeDatabaseAsync();
        var attachmentId = await AddPendingAttachmentAsync(factory);

        await CreateWorker(factory, scanner).ProcessBatchAsync(
            CancellationToken.None);

        var attachment = await GetAttachmentAsync(factory, attachmentId);
        Assert.Equal(AttachmentScanStatus.Pending, attachment.ScanStatus);
        Assert.Equal("retry_exhausted", attachment.ScanFailureCode);
        Assert.Null(attachment.NextScanAttemptAtUtc);
        Assert.Null(attachment.ScanLeaseToken);
    }

    [Theory]
    [InlineData("scanner_unavailable")]
    [InlineData("scan_timeout")]
    [InlineData("invalid_scanner_response")]
    public async Task ProcessBatchAsync_FailsClosedForScannerFailures(
        string failureCode)
    {
        var scanner = new TestScanner();
        scanner.EnqueueFailure(failureCode);
        using var factory = new ApiFactory
        {
            TestAttachmentMalwareScanner = scanner
        };
        await factory.InitializeDatabaseAsync();
        var attachmentId = await AddPendingAttachmentAsync(factory);

        await CreateWorker(factory, scanner).ProcessBatchAsync(
            CancellationToken.None);

        var attachment = await GetAttachmentAsync(factory, attachmentId);
        Assert.Equal(AttachmentScanStatus.Pending, attachment.ScanStatus);
        Assert.Equal(failureCode, attachment.ScanFailureCode);
        Assert.False(attachment.ScanStatus == AttachmentScanStatus.Clean);
    }

    [Fact]
    public async Task ClaimPendingScansAsync_RecoversExpiredLeaseAndRejectsStaleResult()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var attachmentId = await AddPendingAttachmentAsync(factory);
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IRequestAttachmentRepository>();
        var now = DateTime.UtcNow;

        var firstClaim = Assert.Single(await repository.ClaimPendingScansAsync(
            now,
            now.AddMinutes(1),
            maximumAttempts: 6,
            batchSize: 10));
        Assert.Empty(await repository.ClaimPendingScansAsync(
            now,
            now.AddMinutes(1),
            maximumAttempts: 6,
            batchSize: 10));

        var recoveredClaim = Assert.Single(
            await repository.ClaimPendingScansAsync(
                now.AddMinutes(2),
                now.AddMinutes(3),
                maximumAttempts: 6,
                batchSize: 10));
        Assert.Equal(attachmentId, recoveredClaim.AttachmentId);
        Assert.NotEqual(firstClaim.LeaseToken, recoveredClaim.LeaseToken);

        Assert.False(await repository.CompleteScanAsync(
            attachmentId,
            firstClaim.LeaseToken,
            AttachmentScanStatus.Clean,
            "TestScanner",
            DateTime.UtcNow));
        Assert.True(await repository.CompleteScanAsync(
            attachmentId,
            recoveredClaim.LeaseToken,
            AttachmentScanStatus.Clean,
            "TestScanner",
            DateTime.UtcNow));
    }

    [Fact]
    public async Task ProcessBatchAsync_ExhaustsAnExpiredFinalLease()
    {
        var scanner = new TestScanner();
        using var factory = new ApiFactory
        {
            AttachmentScanMaximumAttempts = 1,
            TestAttachmentMalwareScanner = scanner
        };
        await factory.InitializeDatabaseAsync();
        var attachmentId = await AddPendingAttachmentAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<IRequestAttachmentRepository>();
            var now = DateTime.UtcNow;
            Assert.Single(await repository.ClaimPendingScansAsync(
                now,
                now.AddSeconds(1),
                maximumAttempts: 1,
                batchSize: 1));
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            await context.RequestAttachments
                .Where(x => x.Id == attachmentId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        x => x.ScanLeaseExpiresAtUtc,
                        DateTime.UtcNow.AddSeconds(-1)));
        }

        await CreateWorker(factory, scanner).ProcessBatchAsync(
            CancellationToken.None);

        var attachment = await GetAttachmentAsync(factory, attachmentId);
        Assert.Equal(AttachmentScanStatus.Pending, attachment.ScanStatus);
        Assert.Equal("retry_exhausted", attachment.ScanFailureCode);
        Assert.Null(attachment.ScanLeaseToken);
        Assert.Equal(0, scanner.Calls);
    }

    [Fact]
    public async Task ProcessBatchAsync_RejectsStoredHashMismatch()
    {
        var scanner = new TestScanner
        {
            Result = new AttachmentScanResult(
                AttachmentScanStatus.Clean,
                "TestScanner")
        };
        using var factory = new ApiFactory
        {
            TestAttachmentMalwareScanner = scanner
        };
        await factory.InitializeDatabaseAsync();
        var attachmentId = await AddPendingAttachmentAsync(factory);
        var attachment = await GetAttachmentAsync(factory, attachmentId);
        var storage = factory.Services.GetRequiredService<IPrivateFileStorage>();
        await storage.DeleteAsync(
            PrivateFileArea.RequestAttachments,
            attachment.StorageKey,
            attachment.Extension,
            CancellationToken.None);
        await storage.SaveAsync(
            PrivateFileArea.RequestAttachments,
            attachment.StorageKey,
            attachment.Extension,
            new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nchanged")),
            CancellationToken.None);

        await CreateWorker(factory, scanner).ProcessBatchAsync(
            CancellationToken.None);

        attachment = await GetAttachmentAsync(factory, attachmentId);
        Assert.Equal(AttachmentScanStatus.Pending, attachment.ScanStatus);
        Assert.Equal("hash_mismatch", attachment.ScanFailureCode);
        Assert.Equal(0, scanner.Calls);
    }

    [Fact]
    public async Task CleanupWorker_DeletesOnlyOldUnreferencedFiles()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var attachmentId = await AddPendingAttachmentAsync(factory);
        var attachment = await GetAttachmentAsync(factory, attachmentId);
        var storage = factory.Services.GetRequiredService<IPrivateFileStorage>();
        var orphanKey = Guid.NewGuid();
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\norphan");
        await storage.SaveAsync(
            PrivateFileArea.RequestAttachments,
            orphanKey,
            "pdf",
            new MemoryStream(bytes),
            CancellationToken.None);
        var orphanPath = Path.Combine(
            factory.RequestAttachmentStoragePath,
            $"{orphanKey:N}.pdf");
        File.SetLastWriteTimeUtc(orphanPath, DateTime.UtcNow.AddDays(-2));
        var activePath = Path.Combine(
            factory.RequestAttachmentStoragePath,
            $"{attachment.StorageKey:N}.pdf");

        var worker = new RequestAttachmentStorageCleanupWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            storage,
            factory.Services.GetRequiredService<RequestAttachmentOptions>(),
            NullLogger<RequestAttachmentStorageCleanupWorker>.Instance);
        await worker.ProcessBatchAsync(CancellationToken.None);
        await worker.ProcessBatchAsync(CancellationToken.None);

        Assert.False(File.Exists(orphanPath));
        Assert.True(File.Exists(activePath));
    }

    [Fact]
    public async Task CleanupWorker_DoesNotDeleteFileWhileScanLeaseIsActive()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var attachmentId = await AddPendingAttachmentAsync(factory);
        var attachment = await GetAttachmentAsync(factory, attachmentId);
        var path = Path.Combine(
            factory.RequestAttachmentStoragePath,
            $"{attachment.StorageKey:N}.pdf");

        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<IRequestAttachmentRepository>();
            var now = DateTime.UtcNow;
            Assert.Single(await repository.ClaimPendingScansAsync(
                now,
                now.AddMinutes(2),
                maximumAttempts: 6,
                batchSize: 1));
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            var tracked = await context.RequestAttachments
                .SingleAsync(x => x.Id == attachmentId);
            tracked.SoftDelete(attachment.UploadedByUserId, DateTime.UtcNow);
            await context.SaveChangesAsync();
        }

        var worker = new RequestAttachmentStorageCleanupWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<IPrivateFileStorage>(),
            factory.Services.GetRequiredService<RequestAttachmentOptions>(),
            NullLogger<RequestAttachmentStorageCleanupWorker>.Instance);
        await worker.ProcessBatchAsync(CancellationToken.None);
        Assert.True(File.Exists(path));

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            await context.RequestAttachments
                .Where(x => x.Id == attachmentId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        x => x.ScanLeaseExpiresAtUtc,
                        DateTime.UtcNow.AddSeconds(-1)));
        }

        await worker.ProcessBatchAsync(CancellationToken.None);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task CleanupWorker_ContinuesPastOldReferencedFiles()
    {
        using var factory = new ApiFactory
        {
            AttachmentCleanupBatchSize = 1
        };
        await factory.InitializeDatabaseAsync();
        var activeKey = Guid.ParseExact(
            "11111111111111111111111111111111",
            "N");
        await AddPendingAttachmentAsync(factory, activeKey);
        var storage = factory.Services.GetRequiredService<IPrivateFileStorage>();
        var orphanKey = Guid.ParseExact(
            "22222222222222222222222222222222",
            "N");
        await storage.SaveAsync(
            PrivateFileArea.RequestAttachments,
            orphanKey,
            "pdf",
            new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\norphan")),
            CancellationToken.None);
        var activePath = Path.Combine(
            factory.RequestAttachmentStoragePath,
            $"{activeKey:N}.pdf");
        var orphanPath = Path.Combine(
            factory.RequestAttachmentStoragePath,
            $"{orphanKey:N}.pdf");
        File.SetLastWriteTimeUtc(activePath, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(orphanPath, DateTime.UtcNow.AddDays(-2));

        var worker = new RequestAttachmentStorageCleanupWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            storage,
            factory.Services.GetRequiredService<RequestAttachmentOptions>(),
            NullLogger<RequestAttachmentStorageCleanupWorker>.Instance);
        await worker.ProcessBatchAsync(CancellationToken.None);
        Assert.True(File.Exists(activePath));
        Assert.True(File.Exists(orphanPath));
        await worker.ProcessBatchAsync(CancellationToken.None);

        Assert.True(File.Exists(activePath));
        Assert.False(File.Exists(orphanPath));
    }

    private static async Task<long> AddPendingAttachmentAsync(
        ApiFactory factory,
        Guid? specifiedStorageKey = null)
    {
        var owner = await factory.AddUserAsync();
        var request = await factory.AddRequestAsync(owner.Id);
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\nscan test content");
        var storageKey = specifiedStorageKey ?? Guid.NewGuid();
        var storage = factory.Services.GetRequiredService<IPrivateFileStorage>();
        await storage.SaveAsync(
            PrivateFileArea.RequestAttachments,
            storageKey,
            "pdf",
            new MemoryStream(bytes),
            CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attachment = new RequestAttachment(
            request.Id,
            owner.Id,
            storageKey,
            Guid.NewGuid(),
            "scan-test.pdf",
            "pdf",
            "application/pdf",
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)),
            DateTime.UtcNow);
        context.RequestAttachments.Add(attachment);
        await context.SaveChangesAsync();
        return attachment.Id;
    }

    private static RequestAttachmentScanWorker CreateWorker(
        ApiFactory factory,
        IAttachmentMalwareScanner scanner)
    {
        return new RequestAttachmentScanWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<IPrivateFileStorage>(),
            scanner,
            factory.Services.GetRequiredService<RequestAttachmentOptions>(),
            NullLogger<RequestAttachmentScanWorker>.Instance);
    }

    private static async Task<RequestAttachment> GetAttachmentAsync(
        ApiFactory factory,
        long attachmentId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.RequestAttachments
            .AsNoTracking()
            .SingleAsync(x => x.Id == attachmentId);
    }

    private static async Task SetNextAttemptToPastAsync(
        ApiFactory factory,
        long attachmentId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.RequestAttachments
            .Where(x => x.Id == attachmentId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    x => x.NextScanAttemptAtUtc,
                    DateTime.UtcNow.AddSeconds(-1)));
    }

    private sealed class TestScanner : IAttachmentMalwareScanner
    {
        private readonly Queue<object> outcomes = new();

        public AttachmentScanResult Result { get; set; } =
            new(AttachmentScanStatus.Clean, "TestScanner");
        public int Calls { get; private set; }

        public void EnqueueFailure(string failureCode)
        {
            outcomes.Enqueue(new AttachmentScannerException(
                "Synthetic scanner failure.",
                failureCode));
        }

        public void EnqueueResult(AttachmentScanResult result)
        {
            outcomes.Enqueue(result);
        }

        public Task<AttachmentScanResult> ScanAsync(
            Stream content,
            string mediaType,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            var outcome = outcomes.Count > 0
                ? outcomes.Dequeue()
                : Result;
            return outcome switch
            {
                AttachmentScanResult result => Task.FromResult(result),
                Exception exception => Task.FromException<AttachmentScanResult>(
                    exception),
                _ => throw new InvalidOperationException(
                    "The synthetic scanner outcome is invalid.")
            };
        }
    }
}
