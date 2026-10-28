using System.Security.Claims;
using FlowDesk.Application.Services;
using FlowDesk.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowDisk.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoles.Admin)]
[Route("api/admin/audit-logs")]
public sealed class AuditLogsController(AuditLogService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? action = null,
        [FromQuery] string? entityType = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null)
    {
        var actorValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(actorValue, out var actorUserId) ||
            actorUserId <= 0)
        {
            return Forbid();
        }

        try
        {
            return Ok(await service.GetPageAsync(
                actorUserId,
                page,
                pageSize,
                action,
                entityType,
                fromUtc,
                toUtc));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
