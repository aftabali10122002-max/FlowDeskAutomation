using System.Security.Claims;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Services;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowDisk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public sealed class ProfileController(ProfileService profileService)
    : ControllerBase
{
    private const long MaximumImageSizeBytes = 2 * 1024 * 1024;
    private const long MaximumMultipartBodyBytes =
        MaximumImageSizeBytes + 64 * 1024;

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Forbid();
        }

        var profile = await profileService.GetMyProfileAsync(userId.Value);
        return profile is null
            ? Forbid()
            : Ok(profile);
    }

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMyProfile(
        UpdateMyProfileDto dto)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Forbid();
        }

        try
        {
            var profile = await profileService.UpdateMyProfileAsync(
                userId.Value,
                dto.FullName,
                dto.PhoneNumber);
            return profile is null ? Forbid() : Ok(profile);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("me/photo")]
    [RequestSizeLimit(MaximumMultipartBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumMultipartBodyBytes)]
    public async Task<IActionResult> UploadMyProfileImage(
        IFormFile? image,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Forbid();
        }

        if (image is null || image.Length == 0)
        {
            return BadRequest(new { message = "An image file is required." });
        }

        if (image.Length > MaximumImageSizeBytes)
        {
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                new { message = "Profile images cannot exceed 2 MB." });
        }

        try
        {
            await using var stream = image.OpenReadStream();
            await profileService.UpdateMyProfileImageAsync(
                userId.Value,
                stream,
                cancellationToken);
            var profile = await profileService.GetMyProfileAsync(userId.Value);
            return profile is null ? Forbid() : Ok(profile);
        }
        catch (ProfileImageTooLargeException exception)
        {
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                new { message = exception.Message });
        }
        catch (ProfileImageValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpDelete("me/photo")]
    public async Task<IActionResult> RemoveMyProfileImage(
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Forbid();
        }

        try
        {
            await profileService.RemoveMyProfileImageAsync(
                userId.Value,
                cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("users/{id:int}/photo")]
    public async Task<IActionResult> GetProfileImage(
        int id,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Forbid();
        }

        try
        {
            var image = await profileService.GetProfileImageAsync(
                userId.Value,
                id,
                cancellationToken);
            if (image is null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "private, no-store";
            return File(image, "image/jpeg");
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private int? GetAuthenticatedUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var userId) && userId > 0
            ? userId
            : null;
    }
}
