using System.Security.Claims;
using FlowDesk.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowDisk.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Manager,Employee")]
[Route("api/analytics")]
public sealed class DashboardAnalyticsController : ControllerBase
{
    private readonly DashboardAnalyticsService _service;

    public DashboardAnalyticsController(DashboardAnalyticsService service)
    {
        _service = service;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdValue, out var userId) || userId <= 0)
        {
            return Forbid();
        }

        var tokenRole = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrWhiteSpace(tokenRole))
        {
            return Forbid();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rangeFrom = from ?? today.AddDays(-29);
        var rangeTo = to ?? today;
        if (rangeTo < rangeFrom || rangeTo.DayNumber - rangeFrom.DayNumber > 365)
        {
            return BadRequest(new
            {
                message = "The date range must be valid and no longer than 366 days."
            });
        }

        var analytics = await _service.GetAsync(
            userId,
            tokenRole,
            rangeFrom,
            rangeTo);
        return analytics is null ? Forbid() : Ok(analytics);
    }
}
