using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class RequestAttachmentTests
{
    [Fact]
    public async Task Upload_IsIdempotentAndVisibleOnlyToRequestOwner()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await factory.AddUserAsync();
        var otherEmployee = await factory.AddUserAsync();
        var unrelatedManager = await factory.AddUserAsync("Manager");
        var request = await factory.AddRequestAsync(owner.Id);
        using var ownerClient = factory.CreateAuthorizedClient(
            owner.Id,
            "Employee");
        using var otherClient = factory.CreateAuthorizedClient(
            otherEmployee.Id,
            "Employee");
        using var managerClient = factory.CreateAuthorizedClient(
            unrelatedManager.Id,
            "Manager");
        var uploadId = Guid.NewGuid();

        using var firstForm = CreateUpload("%PDF-1.7\n", uploadId);
        using var first = await ownerClient.PostAsync(
            $"/api/requests/{request.Id}/attachments",
            firstForm);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var retryForm = CreateUpload("%PDF-1.7\n", uploadId);
        using var retry = await ownerClient.PostAsync(
            $"/api/requests/{request.Id}/attachments",
            retryForm);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        var attachments = await ownerClient.GetFromJsonAsync<JsonElement[]>(
            $"/api/requests/{request.Id}/attachments");
        var attachment = Assert.Single(attachments!);
        Assert.Equal("Pending", attachment.GetProperty("scanStatus").GetString());
        Assert.False(attachment.GetProperty("isAvailable").GetBoolean());
        var attachmentId = attachment.GetProperty("id").GetInt64();

        using var unavailableDownload = await ownerClient.GetAsync(
            $"/api/requests/{request.Id}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.Conflict, unavailableDownload.StatusCode);

        using var unrelatedList = await otherClient.GetAsync(
            $"/api/requests/{request.Id}/attachments");
        Assert.Equal(HttpStatusCode.NotFound, unrelatedList.StatusCode);
        using var unrelatedManagerList = await managerClient.GetAsync(
            $"/api/requests/{request.Id}/attachments");
        Assert.Equal(HttpStatusCode.NotFound, unrelatedManagerList.StatusCode);
        using var unrelatedDownload = await otherClient.GetAsync(
            $"/api/requests/{request.Id}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.NotFound, unrelatedDownload.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await context.RequestAttachments
            .AsNoTracking()
            .SingleAsync(x => x.RequestId == request.Id);
        Assert.Equal(uploadId, row.ClientUploadId);
        Assert.Equal(owner.Id, row.UploadedByUserId);
        Assert.True(File.Exists(Path.Combine(
            factory.RequestAttachmentStoragePath,
            $"{row.StorageKey:N}.{row.Extension}")));

        using var remove = await ownerClient.DeleteAsync(
            $"/api/requests/{request.Id}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.False(File.Exists(Path.Combine(
            factory.RequestAttachmentStoragePath,
            $"{row.StorageKey:N}.{row.Extension}")));
        var remaining = await ownerClient.GetFromJsonAsync<JsonElement[]>(
            $"/api/requests/{request.Id}/attachments");
        Assert.NotNull(remaining);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task Upload_RejectsSignatureMismatchWithoutPersistingMetadata()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await factory.AddUserAsync();
        var request = await factory.AddRequestAsync(owner.Id);
        using var client = factory.CreateAuthorizedClient(
            owner.Id,
            "Employee");

        using var form = CreateUpload("not a PDF", Guid.NewGuid());
        using var response = await client.PostAsync(
            $"/api/requests/{request.Id}/attachments",
            form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await context.RequestAttachments
            .AnyAsync(x => x.RequestId == request.Id));

        using var extensionMismatchForm = CreateUpload(
            "not an allowed file type",
            Guid.NewGuid(),
            "script.exe");
        using var extensionMismatch = await client.PostAsync(
            $"/api/requests/{request.Id}/attachments",
            extensionMismatchForm);
        Assert.Equal(HttpStatusCode.BadRequest, extensionMismatch.StatusCode);

        using var oversizedForm = CreateUpload(
            new byte[10 * 1024 * 1024 + 1],
            Guid.NewGuid(),
            "large.pdf");
        using var oversized = await client.PostAsync(
            $"/api/requests/{request.Id}/attachments",
            oversizedForm);
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            oversized.StatusCode);
    }

    [Fact]
    public async Task Upload_EnforcesPerUserConcurrentUploadLimit()
    {
        var blockingStorage = new BlockingPrivateFileStorage();
        using var factory = new ApiFactory
        {
            AttachmentUploadConcurrencyLimit = 1,
            TestPrivateFileStorage = blockingStorage
        };
        await factory.InitializeDatabaseAsync();
        var owner = await factory.AddUserAsync();
        var request = await factory.AddRequestAsync(owner.Id);
        using var client = factory.CreateAuthorizedClient(owner.Id, "Employee");
        using var firstForm = CreateUpload("%PDF-1.7\n", Guid.NewGuid());
        var firstTask = client.PostAsync(
            $"/api/requests/{request.Id}/attachments",
            firstForm);
        await blockingStorage.SaveStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        using var second = await client.PostAsync(
            $"/api/requests/{request.Id}/attachments",
            new StringContent("invalid multipart body"));
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);

        blockingStorage.ReleaseSave.TrySetResult();
        using var first = await firstTask;
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
    }

    [Fact]
    public async Task ConcurrentUploadAndApproval_CannotAttachAfterApproval()
    {
        var blockingStorage = new BlockingPrivateFileStorage();
        using var factory = new ApiFactory
        {
            AttachmentUploadConcurrencyLimit = 1,
            TestPrivateFileStorage = blockingStorage
        };
        await factory.InitializeDatabaseAsync();
        var owner = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync("Manager");
        var request = await factory.AddRequestAsync(owner.Id);
        await factory.AddAssignmentAsync(request.Id, manager.Id);
        using var employeeClient = factory.CreateAuthorizedClient(
            owner.Id,
            "Employee");
        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            "Manager");
        using var form = CreateUpload("%PDF-1.7\n", Guid.NewGuid());

        var uploadTask = employeeClient.PostAsync(
            $"/api/requests/{request.Id}/attachments",
            form);
        await blockingStorage.SaveStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        using var approval = await managerClient.PatchAsJsonAsync(
            $"/api/requests/{request.Id}/approve",
            new { comment = "Concurrent upload check" });
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);

        blockingStorage.ReleaseSave.TrySetResult();
        using var upload = await uploadTask;
        Assert.Equal(HttpStatusCode.Conflict, upload.StatusCode);
        Assert.Empty(blockingStorage.Files);
    }

    private static MultipartFormDataContent CreateUpload(
        string content,
        Guid uploadId,
        string fileName = "supporting-document.pdf")
    {
        return CreateUpload(
            Encoding.ASCII.GetBytes(content),
            uploadId,
            fileName);
    }

    private static MultipartFormDataContent CreateUpload(
        byte[] content,
        Guid uploadId,
        string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(uploadId.ToString()), "clientUploadId");
        return form;
    }

    private sealed class BlockingPrivateFileStorage : IPrivateFileStorage
    {
        public Dictionary<Guid, byte[]> Files { get; } = [];

        public TaskCompletionSource SaveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SaveAsync(
            PrivateFileArea area,
            Guid storageKey,
            string extension,
            Stream content,
            CancellationToken cancellationToken)
        {
            SaveStarted.TrySetResult();
            await ReleaseSave.Task.WaitAsync(cancellationToken);
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
            return Task.FromResult(new PrivateFileStoragePage([], null));
        }
    }
}
