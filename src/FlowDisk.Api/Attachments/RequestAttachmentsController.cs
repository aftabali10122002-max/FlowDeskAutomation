using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Application.Services;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using FlowDisk.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowDisk.Api.Attachments;

[ApiController]
[Authorize]
[Route("api/requests")]
public sealed class RequestAttachmentsController(
    ApprovalRequestService requestService,
    IRequestAttachmentRepository attachmentRepository,
    AttachmentIntakeService intake,
    IPrivateFileStorage storage,
    RequestAttachmentOptions options,
    IAuthorizationService authorizationService,
    ILogger<RequestAttachmentsController> logger) : ControllerBase
{
    private const string EmployeeRole = "Employee";
    private const string ManagerRole = "Manager";
    private const string AdminRole = "Admin";
    private static readonly JsonSerializerOptions RequestJsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [HttpPost("with-attachments")]
    [Authorize(Roles = EmployeeRole)]
    [ServiceFilter(typeof(AttachmentUploadConcurrencyResourceFilter))]
    [RequestSizeLimit(RequestAttachmentOptions.MaximumMultipartBodyBytes)]
    [RequestFormLimits(
        MultipartBodyLengthLimit =
            RequestAttachmentOptions.MaximumMultipartBodyBytes)]
    public async Task<IActionResult> CreateWithAttachments(
        [FromForm] string request,
        [FromForm] List<IFormFile> files,
        [FromForm] List<Guid> clientUploadIds,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Forbid();
        }

        if (Request.ContentLength is > RequestAttachmentOptions.MaximumMultipartBodyBytes)
        {
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                new { message = "The upload request is too large." });
        }

        CreateRequestDto? requestDto;
        try
        {
            requestDto = JsonSerializer.Deserialize<CreateRequestDto>(
                request,
                RequestJsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest(new
            {
                message = "The request details are invalid."
            });
        }

        if (requestDto is null ||
            string.IsNullOrWhiteSpace(requestDto.RequestTypeCode))
        {
            return BadRequest(new
            {
                message = "A typed request is required."
            });
        }

        IReadOnlyList<ValidatedAttachment> validated;
        try
        {
            validated = await intake.ValidateFilesAsync(
                files,
                clientUploadIds,
                cancellationToken);
        }
        catch (AttachmentTooLargeException exception)
        {
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                new { message = exception.Message });
        }
        catch (AttachmentRejectedException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (AttachmentValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }

        try
        {
            var stored = await intake.StoreFilesAsync(
                validated,
                cancellationToken);
            try
            {
                var created = await requestService.CreateTypedAsync(
                    requestDto,
                    userId.Value,
                    stored);
                return Created(
                    $"/api/requests/{created.Id}",
                    new { id = created.Id });
            }
            catch (Exception createException)
            {
                try
                {
                    await intake.CleanupStoredFilesAsync(
                        stored,
                        CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(
                        "Request creation failed and stored attachments could not be removed.",
                        createException,
                        cleanupException);
                }

                throw;
            }
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        finally
        {
            DisposeValidated(validated);
        }
    }

    [HttpGet("{requestId:int}/attachments")]
    public async Task<IActionResult> GetAttachments(
        int requestId,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        var request = userId is null
            ? null
            : await GetVisibleRequestAsync(requestId, userId.Value);
        if (request is null)
        {
            return NotFound(new { message = "Request not found." });
        }

        cancellationToken.ThrowIfCancellationRequested();
        var attachments = await attachmentRepository.GetForRequestAsync(
            requestId);
        return Ok(attachments.Select(MapAttachment).ToList());
    }

    [HttpPost("{requestId:int}/attachments")]
    [Authorize(Roles = EmployeeRole)]
    [ServiceFilter(typeof(AttachmentUploadConcurrencyResourceFilter))]
    [RequestSizeLimit(RequestAttachmentOptions.MaximumMultipartBodyBytes)]
    [RequestFormLimits(
        MultipartBodyLengthLimit =
            RequestAttachmentOptions.MaximumMultipartBodyBytes)]
    public async Task<IActionResult> AddAttachment(
        int requestId,
        [FromForm] IFormFile? file,
        [FromForm] Guid clientUploadId,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Forbid();
        }

        if (Request.ContentLength is > RequestAttachmentOptions.MaximumMultipartBodyBytes)
        {
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                new { message = "The upload request is too large." });
        }

        var request = await requestService.GetForEmployeeAsync(
            requestId,
            userId.Value);
        if (request is null)
        {
            return NotFound(new { message = "Request not found." });
        }

        if (request.Status != RequestStatus.Pending)
        {
            return Conflict(new
            {
                message = "Attachments can only be added to pending requests."
            });
        }

        if (file is null)
        {
            return BadRequest(new { message = "A file is required." });
        }

        IReadOnlyList<ValidatedAttachment> validated;
        try
        {
            validated = await intake.ValidateFilesAsync(
                [file],
                [clientUploadId],
                cancellationToken);
        }
        catch (AttachmentTooLargeException exception)
        {
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                new { message = exception.Message });
        }
        catch (AttachmentRejectedException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (AttachmentValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }

        try
        {
            var existing = await attachmentRepository.GetByClientUploadIdAsync(
                clientUploadId);
            if (existing is not null)
            {
                return existing.RequestId == requestId &&
                       existing.UploadedByUserId == userId &&
                       existing.Sha256 == validated[0].Sha256 &&
                       existing.DeletedAtUtc is null
                    ? Ok(MapAttachment(existing))
                    : Conflict(new
                    {
                        message = "The upload identifier has already been used."
                    });
            }

            var stored = await intake.StoreFilesAsync(
                validated,
                cancellationToken);
            try
            {
                var attachment = CreateAttachment(
                    requestId,
                    userId.Value,
                    stored[0]);
                var result =
                    await attachmentRepository.TryAddWithinLimitsAsync(
                        requestId,
                        [attachment],
                        options.MaximumFilesPerRequest,
                        options.MaximumTotalSizeBytes);
                if (result != AttachmentSaveResult.Added)
                {
                    await intake.CleanupStoredFilesAsync(
                        stored,
                        CancellationToken.None);
                    return result switch
                    {
                        AttachmentSaveResult.RequestNoLongerPending =>
                            Conflict(new
                            {
                                message = "Attachments can only be added to pending requests."
                            }),
                        AttachmentSaveResult.FileCountLimitExceeded =>
                            Conflict(new
                            {
                                message = $"A request cannot have more than {options.MaximumFilesPerRequest} files."
                            }),
                        AttachmentSaveResult.TotalSizeLimitExceeded =>
                            StatusCode(
                                StatusCodes.Status413PayloadTooLarge,
                                new
                                {
                                    message = "Attachments exceed the per-request size limit."
                                }),
                        _ => throw new InvalidOperationException(
                            "The attachment repository returned an unsupported save result.")
                    };
                }

                var saved =
                    await attachmentRepository.GetForRequestByIdAsync(
                        requestId,
                        attachment.Id);
                return Created(
                    $"/api/requests/{requestId}/attachments/{attachment.Id}",
                    MapAttachment(saved ?? attachment));
            }
            catch (DbUpdateException saveException)
            {
                try
                {
                    await intake.CleanupStoredFilesAsync(
                        stored,
                        CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(
                        "Attachment metadata could not be saved and the stored file could not be removed.",
                        saveException,
                        cleanupException);
                }

                var duplicate = await attachmentRepository
                    .GetByClientUploadIdAsync(clientUploadId);
                if (duplicate is not null &&
                    duplicate.RequestId == requestId &&
                    duplicate.UploadedByUserId == userId &&
                    duplicate.Sha256 == validated[0].Sha256 &&
                    duplicate.DeletedAtUtc is null)
                {
                    return Ok(MapAttachment(duplicate));
                }

                if (duplicate is not null)
                {
                    return Conflict(new
                    {
                        message = "The upload identifier has already been used."
                    });
                }

                throw;
            }
            catch (Exception saveException)
            {
                try
                {
                    await intake.CleanupStoredFilesAsync(
                        stored,
                        CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(
                        "Attachment metadata could not be saved and the stored file could not be removed.",
                        saveException,
                        cleanupException);
                }

                throw;
            }
        }
        finally
        {
            DisposeValidated(validated);
        }
    }

    [HttpGet("{requestId:int}/attachments/{attachmentId:long}")]
    public Task<IActionResult> Download(
        int requestId,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        return GetContentAsync(
            requestId,
            attachmentId,
            preview: false,
            cancellationToken);
    }

    [HttpGet("{requestId:int}/attachments/{attachmentId:long}/preview")]
    public Task<IActionResult> Preview(
        int requestId,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        return GetContentAsync(
            requestId,
            attachmentId,
            preview: true,
            cancellationToken);
    }

    [HttpDelete("{requestId:int}/attachments/{attachmentId:long}")]
    public async Task<IActionResult> Delete(
        int requestId,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        var request = userId is null
            ? null
            : await GetVisibleRequestAsync(requestId, userId.Value);
        if (request is null)
        {
            return NotFound(new { message = "Request not found." });
        }

        var isOwner = request.CreatedByUserId == userId;
        var isAdmin = User.IsInRole(AdminRole);
        if (!isOwner && !isAdmin)
        {
            return NotFound(new { message = "Attachment not found." });
        }

        if (isOwner &&
            request.Status != RequestStatus.Pending)
        {
            return Conflict(new
            {
                message = "Attachments can only be removed from pending requests."
            });
        }

        var attachment =
            await attachmentRepository.GetForRequestByIdAsync(
                requestId,
                attachmentId);
        if (attachment is null)
        {
            return NotFound(new { message = "Attachment not found." });
        }

        if (isOwner)
        {
            var deleted = await attachmentRepository
            .TrySoftDeleteOwnedPendingAsync(
                requestId,
                attachmentId,
                userId!.Value,
                DateTime.UtcNow);
            if (!deleted)
            {
            return Conflict(new
            {
                message = "Attachments can only be removed from pending requests."
            });
            }
        }
        else
        {
            attachment.SoftDelete(userId!.Value, DateTime.UtcNow);
            await attachmentRepository.SaveChangesAsync();
        }

        try
        {
            await storage.DeleteAsync(
                PrivateFileArea.RequestAttachments,
                attachment.StorageKey,
                attachment.Extension,
                cancellationToken);
            attachment.MarkStorageCleanupComplete();
            await attachmentRepository.SaveChangesAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Attachment {AttachmentId} was removed from view but storage cleanup is pending.",
                attachment.Id);
        }

        return NoContent();
    }

    private async Task<IActionResult> GetContentAsync(
        int requestId,
        long attachmentId,
        bool preview,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        var request = userId is null
            ? null
            : await GetVisibleRequestAsync(requestId, userId.Value);
        if (request is null)
        {
            return NotFound(new { message = "Request not found." });
        }

        var attachment =
            await attachmentRepository.GetForRequestByIdAsync(
                requestId,
                attachmentId);
        if (attachment is null)
        {
            return NotFound(new { message = "Attachment not found." });
        }

        var available = attachment.ScanStatus == AttachmentScanStatus.Clean;
        if (!available)
        {
            return Conflict(new
            {
                message = attachment.ScanStatus == AttachmentScanStatus.Rejected
                    ? "This file was rejected by the malware scanner."
                    : "This file is unavailable until an approved scan completes."
            });
        }

        if (preview &&
            attachment.MediaType is not "application/pdf" and not "image/jpeg" and not "image/png")
        {
            return BadRequest(new
            {
                message = "Preview is only available for PDF, JPEG, and PNG files."
            });
        }

        var stream = await storage.OpenReadAsync(
            PrivateFileArea.RequestAttachments,
            attachment.StorageKey,
            attachment.Extension,
            cancellationToken);
        if (stream is null)
        {
            return NotFound(new { message = "Attachment content was not found." });
        }

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        if (preview)
        {
            Response.Headers.ContentSecurityPolicy =
                "sandbox; default-src 'none'; frame-ancestors 'self'";
            Response.Headers.ContentDisposition =
                BuildContentDisposition(attachment.OriginalFileName, inline: true);
            return File(stream, attachment.MediaType);
        }

        Response.Headers.ContentDisposition =
            BuildContentDisposition(attachment.OriginalFileName, inline: false);
        return File(stream, attachment.MediaType);
    }

    private async Task<ApprovalRequest?> GetVisibleRequestAsync(
        int requestId,
        int userId)
    {
        if (User.IsInRole(AdminRole))
        {
            if (await IsAuthorizedForPolicyAsync(RequestPolicies.ReadAllRequests))
            {
                var owned = await requestService.GetForAdminAsync(
                    requestId,
                    userId);
                if (owned is not null)
                {
                    return owned;
                }
            }

            return await IsAuthorizedForPolicyAsync(
                    RequestPolicies.ReviewLegacyRequests)
                ? await requestService.GetLegacyForAdminAsync(requestId, userId)
                : null;
        }

        if (User.IsInRole(ManagerRole))
        {
            return await requestService.GetAssignedToManagerAsync(
                requestId,
                userId);
        }

        return User.IsInRole(EmployeeRole)
            ? await requestService.GetForEmployeeAsync(requestId, userId)
            : null;
    }

    private async Task<bool> IsAuthorizedForPolicyAsync(string policy)
    {
        var result = await authorizationService.AuthorizeAsync(User, policy);
        return result.Succeeded;
    }

    private AttachmentResponseDto MapAttachment(RequestAttachment attachment)
    {
        var canAccess = attachment.ScanStatus == AttachmentScanStatus.Clean;
        return new AttachmentResponseDto
        {
            Id = attachment.Id,
            RequestId = attachment.RequestId,
            FileName = attachment.OriginalFileName,
            Extension = attachment.Extension,
            MediaType = attachment.MediaType,
            SizeBytes = attachment.SizeBytes,
            UploadedAtUtc = attachment.UploadedAtUtc,
            UploadedBy = attachment.UploadedByUser?.FullName ?? string.Empty,
            ScanStatus = attachment.ScanStatus.ToString(),
            IsAvailable = canAccess
        };
    }

    private static RequestAttachment CreateAttachment(
        int requestId,
        int uploaderUserId,
        PreparedRequestAttachment prepared)
    {
        var attachment = new RequestAttachment(
            requestId,
            uploaderUserId,
            prepared.StorageKey,
            prepared.ClientUploadId,
            prepared.OriginalFileName,
            prepared.Extension,
            prepared.MediaType,
            prepared.SizeBytes,
            prepared.Sha256,
            DateTime.UtcNow);
        ApplyScanResult(attachment, prepared);
        return attachment;
    }

    private static void ApplyScanResult(
        RequestAttachment attachment,
        PreparedRequestAttachment prepared)
    {
        switch (prepared.ScanStatus)
        {
            case AttachmentScanStatus.Clean:
                attachment.MarkScanned(
                    true,
                    prepared.ScannerName
                        ?? throw new InvalidOperationException(
                            "A clean scan result must include a scanner name."),
                    prepared.ScannedAtUtc
                        ?? throw new InvalidOperationException(
                            "A clean scan result must include a scan time."));
                break;
            case AttachmentScanStatus.NotScanned:
                attachment.MarkNotScanned(
                    prepared.ScannedAtUtc
                        ?? throw new InvalidOperationException(
                            "An unscanned result must include a completion time."));
                break;
            case AttachmentScanStatus.Pending:
                break;
            default:
                throw new InvalidOperationException(
                    "The attachment scan status is invalid.");
        }
    }

    private static string BuildContentDisposition(
        string fileName,
        bool inline)
    {
        var safeName = string.Concat(fileName.Where(character =>
            !char.IsControl(character) &&
            character is not '"' and not '\\' and not '/'));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "attachment";
        }

        return $"{(inline ? "inline" : "attachment")}; " +
            $"filename=\"{safeName}\"; " +
            $"filename*=UTF-8''{Uri.EscapeDataString(safeName)}";
    }

    private int? GetAuthenticatedUserId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claimValue, out var userId) && userId > 0
            ? userId
            : null;
    }

    private static void DisposeValidated(
        IEnumerable<ValidatedAttachment> validated)
    {
        foreach (var attachment in validated)
        {
            attachment.Dispose();
        }
    }
}

public sealed class AttachmentResponseDto
{
    public long Id { get; set; }
    public int RequestId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public string ScanStatus { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
}
