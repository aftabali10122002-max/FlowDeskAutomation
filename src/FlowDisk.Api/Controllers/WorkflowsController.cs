using System.Security.Claims;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Services;
using FlowDesk.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowDisk.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoles.Admin)]
[Route("api/admin/workflows")]
public sealed class WorkflowsController(
    ApprovalWorkflowService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var adminId = GetAuthenticatedUserId();
        if (adminId is null)
        {
            return Forbid();
        }

        var rules = await service.GetRulesAsync(adminId.Value);
        return Ok(rules.Select(MapRule).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var adminId = GetAuthenticatedUserId();
        if (adminId is null)
        {
            return Forbid();
        }

        var rule = await service.GetRuleAsync(adminId.Value, id);
        return rule is null
            ? NotFound(new { message = "Workflow rule not found." })
            : Ok(MapRule(rule));
    }

    [HttpPost]
    public async Task<IActionResult> Create(WorkflowRuleInputDto input)
    {
        var adminId = GetAuthenticatedUserId();
        if (adminId is null)
        {
            return Forbid();
        }

        var rule = await service.CreateRuleAsync(adminId.Value, input);
        return CreatedAtAction(
            nameof(Get),
            new { id = rule.Id },
            MapRule(rule));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateDraft(
        int id,
        WorkflowRuleInputDto input)
    {
        var adminId = GetAuthenticatedUserId();
        if (adminId is null)
        {
            return Forbid();
        }

        var rule = await service.UpdateDraftAsync(
            adminId.Value,
            id,
            input);
        return rule is null
            ? NotFound(new { message = "Workflow rule not found." })
            : Ok(MapRule(rule));
    }

    [HttpPost("{id:int}/versions")]
    public async Task<IActionResult> CreateVersion(int id)
    {
        var adminId = GetAuthenticatedUserId();
        if (adminId is null)
        {
            return Forbid();
        }

        var rule = await service.CreateVersionAsync(adminId.Value, id);
        return rule is null
            ? NotFound(new { message = "Workflow rule not found." })
            : CreatedAtAction(nameof(Get), new { id = rule.Id }, MapRule(rule));
    }

    [HttpPost("{id:int}/activate")]
    public async Task<IActionResult> Activate(int id)
    {
        var adminId = GetAuthenticatedUserId();
        if (adminId is null)
        {
            return Forbid();
        }

        await service.ActivateRuleAsync(adminId.Value, id);
        return NoContent();
    }

    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var adminId = GetAuthenticatedUserId();
        if (adminId is null)
        {
            return Forbid();
        }

        await service.DeactivateRuleAsync(adminId.Value, id);
        return NoContent();
    }

    [HttpPost("process-escalations")]
    public async Task<IActionResult> ProcessEscalations()
    {
        var adminId = GetAuthenticatedUserId();
        if (adminId is null)
        {
            return Forbid();
        }

        var processed = await service.ProcessEscalationsAsync(adminId.Value);
        return Ok(new { processed });
    }

    private int? GetAuthenticatedUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) && id > 0 ? id : null;
    }

    private static WorkflowRuleResponseDto MapRule(
        ApprovalWorkflowRule rule)
    {
        return new WorkflowRuleResponseDto
        {
            Id = rule.Id,
            WorkflowKey = rule.WorkflowKey,
            Name = rule.Name,
            Version = rule.Version,
            RequestTypeId = rule.RequestTypeId,
            RequestTypeCode = rule.RequestType?.Code,
            RequestTypeName = rule.RequestType?.DisplayName,
            DepartmentId = rule.DepartmentId,
            DepartmentName = rule.Department?.Name,
            MinimumAmount = rule.MinimumAmount,
            MaximumAmountExclusive = rule.MaximumAmountExclusive,
            Priority = rule.Priority,
            Status = rule.Status.ToString(),
            CreatedAtUtc = DateTime.SpecifyKind(
                rule.CreatedAtUtc,
                DateTimeKind.Utc),
            PublishedAtUtc = rule.PublishedAtUtc.HasValue
                ? DateTime.SpecifyKind(
                    rule.PublishedAtUtc.Value,
                    DateTimeKind.Utc)
                : null,
            Stages = rule.Stages
                .OrderBy(x => x.Sequence)
                .Select(x => new WorkflowRuleStageDto
                {
                    Sequence = x.Sequence,
                    ApproverType = x.ApproverType,
                    ApproverDepartmentId = x.ApproverDepartmentId,
                    SpecificApproverUserId = x.SpecificApproverUserId,
                    DeadlineMinutes = x.DeadlineMinutes,
                    EscalationAfterMinutes = x.EscalationAfterMinutes,
                    EscalationApproverUserId = x.EscalationApproverUserId
                })
                .ToList()
        };
    }
}
