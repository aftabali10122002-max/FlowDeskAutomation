using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.DTOs;

public sealed class WorkflowRuleStageDto
{
    public int Sequence { get; set; }
    public WorkflowApproverType ApproverType { get; set; }
    public int? ApproverDepartmentId { get; set; }
    public int? SpecificApproverUserId { get; set; }
    public int? DeadlineMinutes { get; set; }
    public int? EscalationAfterMinutes { get; set; }
    public int? EscalationApproverUserId { get; set; }
}

public sealed class WorkflowRuleInputDto
{
    public string WorkflowKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? RequestTypeId { get; set; }
    public int? DepartmentId { get; set; }
    public decimal MinimumAmount { get; set; }
    public decimal? MaximumAmountExclusive { get; set; }
    public RequestPriority? Priority { get; set; }
    public List<WorkflowRuleStageDto> Stages { get; set; } = [];
}

public sealed class WorkflowRuleResponseDto
{
    public int Id { get; set; }
    public string WorkflowKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public int? RequestTypeId { get; set; }
    public string? RequestTypeCode { get; set; }
    public string? RequestTypeName { get; set; }
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public decimal MinimumAmount { get; set; }
    public decimal? MaximumAmountExclusive { get; set; }
    public RequestPriority? Priority { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public List<WorkflowRuleStageDto> Stages { get; set; } = [];
}

public sealed class WorkflowStageResponseDto
{
    public long Id { get; set; }
    public int Sequence { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ApproverUserId { get; set; }
    public string ApproverName { get; set; } = string.Empty;
    public int? DeadlineMinutes { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? Decision { get; set; }
    public string? Comment { get; set; }
    public DateTime? EscalatedAtUtc { get; set; }
}

public sealed class WorkflowInstanceResponseDto
{
    public int RequestId { get; set; }
    public string WorkflowName { get; set; } = string.Empty;
    public int WorkflowVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public List<WorkflowStageResponseDto> Stages { get; set; } = [];
}

public sealed class WorkflowNotificationResponseDto
{
    public long Id { get; set; }
    public int? RequestId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

public sealed class WorkflowNotificationPageResponseDto
{
    public List<WorkflowNotificationResponseDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int UnreadCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
