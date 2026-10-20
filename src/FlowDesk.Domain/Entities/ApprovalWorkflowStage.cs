using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public sealed class ApprovalWorkflowStage
{
    public long Id { get; private set; }
    public long WorkflowInstanceId { get; private set; }
    public ApprovalWorkflowInstance WorkflowInstance { get; private set; } = null!;
    public int Sequence { get; private set; }
    public ApprovalWorkflowStageStatus Status { get; private set; }
        = ApprovalWorkflowStageStatus.Inactive;
    public int ApproverUserId { get; private set; }
    public User ApproverUser { get; private set; } = null!;
    public int? DeadlineMinutes { get; private set; }
    public int? EscalationAfterMinutes { get; private set; }
    public int? EscalationApproverUserId { get; private set; }
    public User? EscalationApproverUser { get; private set; }
    public DateTime? ActivatedAtUtc { get; private set; }
    public DateTime? DueAtUtc { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }

    public ApprovalDecisionHistory? DecisionHistory { get; private set; }

    public ICollection<ApprovalWorkflowEscalation> Escalations { get; private set; }
        = new List<ApprovalWorkflowEscalation>();

    private ApprovalWorkflowStage() { }

    public ApprovalWorkflowStage(
        int sequence,
        int approverUserId,
        int? deadlineMinutes,
        int? escalationAfterMinutes,
        int? escalationApproverUserId)
    {
        if (sequence <= 0 || approverUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        Sequence = sequence;
        ApproverUserId = approverUserId;
        DeadlineMinutes = deadlineMinutes;
        EscalationAfterMinutes = escalationAfterMinutes;
        EscalationApproverUserId = escalationApproverUserId;
    }

    public void Activate(DateTime activatedAtUtc)
    {
        if (Status != ApprovalWorkflowStageStatus.Inactive)
        {
            throw new InvalidOperationException(
                "Only an inactive stage can be activated.");
        }

        EnsureUtc(activatedAtUtc);
        Status = ApprovalWorkflowStageStatus.Active;
        ActivatedAtUtc = activatedAtUtc;
        DueAtUtc = DeadlineMinutes.HasValue
            ? activatedAtUtc.AddMinutes(DeadlineMinutes.Value)
            : null;
    }

    public void Decide(ApprovalWorkflowStageStatus decision, DateTime decidedAtUtc)
    {
        if (Status != ApprovalWorkflowStageStatus.Active ||
            decision is not ApprovalWorkflowStageStatus.Approved and
                not ApprovalWorkflowStageStatus.Rejected)
        {
            throw new InvalidOperationException(
                "Only the active stage can be approved or rejected.");
        }

        EnsureUtc(decidedAtUtc);
        Status = decision;
        DecidedAtUtc = decidedAtUtc;
    }

    public void Cancel(DateTime cancelledAtUtc)
    {
        if (Status == ApprovalWorkflowStageStatus.Approved ||
            Status == ApprovalWorkflowStageStatus.Rejected ||
            Status == ApprovalWorkflowStageStatus.Cancelled)
        {
            return;
        }

        EnsureUtc(cancelledAtUtc);
        Status = ApprovalWorkflowStageStatus.Cancelled;
        DecidedAtUtc = cancelledAtUtc;
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be UTC.");
        }
    }
}
