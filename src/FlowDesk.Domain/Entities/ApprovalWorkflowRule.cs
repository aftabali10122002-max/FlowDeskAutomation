using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public sealed class ApprovalWorkflowRule
{
    public int Id { get; private set; }
    public string WorkflowKey { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public int? DepartmentId { get; private set; }
    public Department? Department { get; private set; }
    public decimal MinimumAmount { get; private set; }
    public decimal? MaximumAmountExclusive { get; private set; }
    public RequestPriority? Priority { get; private set; }
    public ApprovalWorkflowRuleStatus Status { get; private set; }
        = ApprovalWorkflowRuleStatus.Draft;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; private set; }
    public ICollection<ApprovalWorkflowRuleStage> Stages { get; private set; }
        = new List<ApprovalWorkflowRuleStage>();

    private ApprovalWorkflowRule() { }

    public ApprovalWorkflowRule(
        string workflowKey,
        string name,
        int version,
        int? departmentId,
        decimal minimumAmount,
        decimal? maximumAmountExclusive,
        RequestPriority? priority,
        IEnumerable<ApprovalWorkflowRuleStage> stages)
    {
        UpdateDraft(
            workflowKey,
            name,
            departmentId,
            minimumAmount,
            maximumAmountExclusive,
            priority,
            stages);
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        Version = version;
    }

    public void UpdateDraft(
        string workflowKey,
        string name,
        int? departmentId,
        decimal minimumAmount,
        decimal? maximumAmountExclusive,
        RequestPriority? priority,
        IEnumerable<ApprovalWorkflowRuleStage> stages)
    {
        if (Status != ApprovalWorkflowRuleStatus.Draft)
        {
            throw new InvalidOperationException(
                "Only draft workflow rules can be edited.");
        }

        if (string.IsNullOrWhiteSpace(workflowKey) ||
            workflowKey.Trim().Length > 100)
        {
            throw new ArgumentException(
                "Workflow key is required and cannot exceed 100 characters.",
                nameof(workflowKey));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150)
        {
            throw new ArgumentException(
                "Workflow name is required and cannot exceed 150 characters.",
                nameof(name));
        }

        if (departmentId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(departmentId));
        }

        if (minimumAmount < 0 ||
            maximumAmountExclusive is <= 0 ||
            (maximumAmountExclusive.HasValue &&
             maximumAmountExclusive.Value <= minimumAmount))
        {
            throw new ArgumentException(
                "Workflow amount range is invalid.");
        }

        if (priority.HasValue && !Enum.IsDefined(priority.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        ArgumentNullException.ThrowIfNull(stages);
        var stageList = stages.OrderBy(x => x.Sequence).ToList();
        if (stageList.Count is < 1 or > 10 ||
            stageList.Select(x => x.Sequence)
                .Where((sequence, index) => sequence != index + 1)
                .Any())
        {
            throw new ArgumentException(
                "A workflow must contain 1 to 10 sequential stages.",
                nameof(stages));
        }

        WorkflowKey = workflowKey.Trim();
        Name = name.Trim();
        DepartmentId = departmentId;
        MinimumAmount = minimumAmount;
        MaximumAmountExclusive = maximumAmountExclusive;
        Priority = priority;
        Stages = stageList;
    }

    public void Activate(DateTime publishedAtUtc)
    {
        if (Status != ApprovalWorkflowRuleStatus.Draft ||
            Stages.Count == 0)
        {
            throw new InvalidOperationException(
                "Only a configured draft can be activated.");
        }

        EnsureUtc(publishedAtUtc);
        Status = ApprovalWorkflowRuleStatus.Active;
        PublishedAtUtc = publishedAtUtc;
    }

    public void Deactivate()
    {
        if (Status != ApprovalWorkflowRuleStatus.Active)
        {
            throw new InvalidOperationException(
                "Only active workflow rules can be deactivated.");
        }

        Status = ApprovalWorkflowRuleStatus.Inactive;
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be UTC.");
        }
    }
}
