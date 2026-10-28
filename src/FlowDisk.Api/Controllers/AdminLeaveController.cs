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
[Route("api/admin/leave")]
public sealed class AdminLeaveController(
    ILeavePolicyService policyService,
    IEmployeeEmploymentProfileService employmentProfileService,
    LeaveAccountingService accountingService) : ControllerBase
{
    [HttpGet("policies")]
    public async Task<IActionResult> GetPolicies()
    {
        var policies = await policyService.GetPoliciesAsync(GetAdminUserId());
        return Ok(policies.Select(ToPolicy));
    }

    [HttpPost("policies")]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] LeavePolicyDraftInput input)
    {
        var policy = await policyService.CreateDraftAsync(
            GetAdminUserId(),
            input);
        return CreatedAtAction(
            nameof(GetPolicies),
            new { },
            ToPolicy(policy));
    }

    [HttpPut("policies/{policyId:int}")]
    public async Task<IActionResult> UpdateDraft(
        int policyId,
        [FromBody] LeavePolicyDraftInput input)
    {
        var policy = await policyService.UpdateDraftAsync(
            GetAdminUserId(),
            policyId,
            input);
        return Ok(ToPolicy(policy));
    }

    [HttpPost("policies/{policyId:int}/publish")]
    public async Task<IActionResult> Publish(int policyId)
    {
        await policyService.PublishAsync(GetAdminUserId(), policyId);
        return NoContent();
    }

    [HttpPost("policies/{policyId:int}/supersede")]
    public async Task<IActionResult> Supersede(
        int policyId,
        [FromBody] SupersedeLeavePolicyInput input)
    {
        await policyService.MarkSupersededAsync(
            GetAdminUserId(),
            policyId,
            input.SuccessorPolicyId);
        return NoContent();
    }

    [HttpPost("policies/{policyId:int}/versions")]
    public async Task<IActionResult> CreateVersion(
        int policyId,
        [FromBody] CreateLeavePolicyVersionInput input)
    {
        var draft = await policyService.CreateVersionDraftAsync(
            GetAdminUserId(),
            policyId,
            input.EffectiveFrom);
        return CreatedAtAction(
            nameof(GetPolicies),
            new { },
            ToPolicy(draft));
    }

    [HttpPost("assignments")]
    public async Task<IActionResult> AssignPolicy(
        [FromBody] LeavePolicyAssignmentInput input)
    {
        await policyService.AssignPolicyAsync(
            GetAdminUserId(),
            input.EmployeeUserId,
            input.LeavePolicyId,
            input.EffectiveFrom,
            input.EffectiveTo);
        return NoContent();
    }

    [HttpGet("employees/{employeeUserId:int}/metadata")]
    public async Task<IActionResult> GetEmployeeMetadata(
        int employeeUserId,
        [FromQuery] DateOnly? asOf)
    {
        return Ok(await employmentProfileService.GetMetadataAsync(
            GetAdminUserId(),
            employeeUserId,
            asOf ?? DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    [HttpPost("employees/metadata")]
    public async Task<IActionResult> AssignEmployeeMetadata(
        [FromBody] EmployeeEmploymentProfileInput input)
    {
        await employmentProfileService.AssignMetadataAsync(
            GetAdminUserId(),
            input);
        return NoContent();
    }

    [HttpGet("types")]
    public async Task<IActionResult> GetTypes()
    {
        return Ok(await accountingService.GetAllTypesAsync(
            GetAdminUserId()));
    }

    [HttpPost("types")]
    public async Task<IActionResult> CreateType(
        [FromBody] LeaveTypeInput input)
    {
        var leaveType = await accountingService.CreateTypeAsync(
            GetAdminUserId(),
            input);
        return CreatedAtAction(nameof(GetTypes), new { }, leaveType);
    }

    [HttpPut("types/{leaveTypeId:int}")]
    public async Task<IActionResult> UpdateType(
        int leaveTypeId,
        [FromBody] LeaveTypeUpdateInput input)
    {
        return Ok(await accountingService.UpdateTypeAsync(
            GetAdminUserId(),
            leaveTypeId,
            input));
    }

    [HttpGet("employees/{employeeUserId:int}/balances")]
    public async Task<IActionResult> GetEmployeeBalances(
        int employeeUserId,
        [FromQuery] DateOnly? asOf)
    {
        return Ok(await accountingService.GetBalancesAsync(
            GetAdminUserId(),
            employeeUserId,
            asOf ?? DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    [HttpPost("adjustments")]
    public async Task<IActionResult> CreateAdjustment(
        [FromBody] LeaveManualAdjustmentInput input)
    {
        var created = await accountingService.AdjustBalanceAsync(
            GetAdminUserId(),
            input);
        return created ? Ok() : Conflict(new
        {
            message = "The adjustment idempotency key has already been used."
        });
    }

    [HttpGet("adjustments")]
    public async Task<IActionResult> GetAdjustmentAudit(
        [FromQuery] int employeeUserId,
        [FromQuery] DateOnly? throughDate)
    {
        var entries = await accountingService.GetHistoryAsync(
            GetAdminUserId(),
            employeeUserId,
            null,
            throughDate);
        return Ok(entries.Where(entry =>
            entry.EntryType is
                FlowDesk.Domain.Enums.LeaveLedgerEntryType.Adjustment or
                FlowDesk.Domain.Enums.LeaveLedgerEntryType.Reversal));
    }

    private int GetAdminUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(value, out var userId) || userId <= 0)
        {
            throw new LeaveUnauthorizedException();
        }

        return userId;
    }

    private static LeavePolicyResponse ToPolicy(
        FlowDesk.Domain.Entities.LeavePolicy policy) =>
        new(
            policy.Id,
            policy.Code,
            policy.Name,
            policy.Version,
            policy.Status,
            policy.Scope,
            policy.EffectiveFrom,
            policy.EffectiveTo,
            policy.Entitlements
                .Select(entitlement =>
                    new LeavePolicyEntitlementResponse(
                        entitlement.LeaveTypeId,
                        entitlement.AnnualEntitlement))
                .ToList());
}

public sealed record CreateLeavePolicyVersionInput(DateOnly EffectiveFrom);
public sealed record SupersedeLeavePolicyInput(int SuccessorPolicyId);
