using System.Security.Claims;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowDisk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController(
    ApprovalWorkflowService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Forbid();
        }

        var notifications = await service.GetNotificationsAsync(
            userId.Value,
            page,
            pageSize);
        return Ok(new WorkflowNotificationPageResponseDto
        {
            Items = notifications.Items.Select(x =>
                new WorkflowNotificationResponseDto
                {
                    Id = x.Id,
                    RequestId = x.RequestId,
                    Type = x.Type.ToString(),
                    Message = x.Message,
                    CreatedAtUtc = DateTime.SpecifyKind(
                        x.CreatedAtUtc,
                        DateTimeKind.Utc),
                    ReadAtUtc = x.ReadAtUtc.HasValue
                        ? DateTime.SpecifyKind(
                            x.ReadAtUtc.Value,
                            DateTimeKind.Utc)
                        : null
                }).ToList(),
            TotalCount = notifications.TotalCount,
            UnreadCount = notifications.UnreadCount,
            Page = notifications.Page,
            PageSize = notifications.PageSize
        });
    }

    [HttpPatch("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Forbid();
        }

        return await service.MarkNotificationReadAsync(userId.Value, id)
            ? NoContent()
            : NotFound(new { message = "Notification not found." });
    }

    private int? GetAuthenticatedUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) && id > 0 ? id : null;
    }
}
