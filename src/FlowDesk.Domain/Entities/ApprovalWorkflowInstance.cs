using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public sealed class ApprovalWorkflowInstance
{
    public long Id { get; private set; }
    public int RequestId { get; private set; }
    public ApprovalRequest Request { get; private set; } = null!;
    public int WorkflowRuleId { get; private set; }
    public ApprovalWorkflowRule WorkflowRule { get; private set; } = null!;
    public int RuleVersion { get; private set; }
    public ApprovalWorkflowStatus Status { get; private set; }
        = ApprovalWorkflowStatus.Active;
    public DateTime StartedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; private set; }
    public ICollection<ApprovalWorkflowStage> Stages { get; private set; }
        = new List<ApprovalWorkflowStage>();

    private ApprovalWorkflowInstance() { }

    public ApprovalWorkflowInstance(
        int requestId,
        ApprovalWorkflowRule rule,
        IEnumerable<ApprovalWorkflowStage> stages,
        DateTime startedAtUtc)
    {
        if (requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(stages);
        if (rule.Status != ApprovalWorkflowRuleStatus.Active)
        {
            throw new InvalidOperationException(
                "Only an active rule can create a workflow instance.");
        }

        if (startedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Workflow start time must be UTC.",
                nameof(startedAtUtc));
        }

        RequestId = requestId;
        WorkflowRuleId = rule.Id;
        RuleVersion = rule.Version;
        StartedAtUtc = startedAtUtc;
        Stages = stages.OrderBy(x => x.Sequence).ToList();
    }

    public void Complete(ApprovalWorkflowStatus status, DateTime completedAtUtc)
    {
        if (Status != ApprovalWorkflowStatus.Active ||
            status == ApprovalWorkflowStatus.Active)
        {
            throw new InvalidOperationException(
                "Only an active workflow can be completed.");
        }

        if (completedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Workflow completion time must be UTC.",
                nameof(completedAtUtc));
        }

        Status = status;
        CompletedAtUtc = completedAtUtc;
    }
}
