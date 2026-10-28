using System.Security.Claims;
using FlowDesk.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowDisk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/request-types")]
public sealed class RequestTypesController(
    RequestTypeService requestTypeService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetEnabled()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(claim, out var userId) || userId <= 0)
        {
            return Forbid();
        }

        return Ok(await requestTypeService.GetEnabledForUserAsync(userId));
    }
}
