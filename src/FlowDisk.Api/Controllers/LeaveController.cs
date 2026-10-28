using System.Security.Claims;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Errors;
using FlowDesk.Application.Interfaces;
using FlowDesk.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowDisk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/leave")]
public sealed class LeaveController(
    LeaveAccountingService accountingService,
    ILeaveRequestPreparationService requestPreparationService) : ControllerBase
{
    [HttpPost("me/preview")]
    public async Task<IActionResult> PreviewMyLeave(
        [FromBody] LeaveRequestPreviewInput input)
    {
        return Ok(await requestPreparationService.PreviewAsync(
            GetActorUserId(),
            input));
    }

    [HttpGet("types")]
    public async Task<IActionResult> GetTypes()
    {
        var actorUserId = GetActorUserId();
        return Ok(await accountingService.GetTypesAsync(actorUserId));
    }

    [HttpGet("me/balances")]
    public async Task<IActionResult> GetMyBalances(
        [FromQuery] DateOnly? asOf)
    {
        var actorUserId = GetActorUserId();
        return Ok(await accountingService.GetBalancesAsync(
            actorUserId,
            actorUserId,
            asOf ?? DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    [HttpGet("me/history")]
    public async Task<IActionResult> GetMyHistory(
        [FromQuery] int? leaveTypeId,
        [FromQuery] DateOnly? throughDate)
    {
        var actorUserId = GetActorUserId();
        return Ok(await accountingService.GetHistoryAsync(
            actorUserId,
            actorUserId,
            leaveTypeId,
            throughDate));
    }

    [HttpGet("employees/{employeeUserId:int}/balances")]
    public async Task<IActionResult> GetEmployeeBalances(
        int employeeUserId,
        [FromQuery] DateOnly? asOf)
    {
        return Ok(await accountingService.GetBalancesAsync(
            GetActorUserId(),
            employeeUserId,
            asOf ?? DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    [HttpGet("employees/{employeeUserId:int}/history")]
    public async Task<IActionResult> GetEmployeeHistory(
        int employeeUserId,
        [FromQuery] int? leaveTypeId,
        [FromQuery] DateOnly? throughDate)
    {
        return Ok(await accountingService.GetHistoryAsync(
            GetActorUserId(),
            employeeUserId,
            leaveTypeId,
            throughDate));
    }

    [HttpGet("manager/team")]
    public async Task<IActionResult> GetTeamBalances(
        [FromQuery] DateOnly? asOf)
    {
        return Ok(await accountingService.GetTeamBalancesAsync(
            GetActorUserId(),
            asOf ?? DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    private int GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(value, out var userId) || userId <= 0)
        {
            throw new LeaveUnauthorizedException();
        }

        return userId;
    }
}
