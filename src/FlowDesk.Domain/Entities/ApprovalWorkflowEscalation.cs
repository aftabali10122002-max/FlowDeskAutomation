namespace FlowDesk.Domain.Entities;

public sealed class ApprovalWorkflowEscalation
{
    public long Id { get; private set; }
    public long WorkflowStageId { get; private set; }
    public ApprovalWorkflowStage WorkflowStage { get; private set; } = null!;
    public int EscalationNumber { get; private set; }
    public int RecipientUserId { get; private set; }
    public User RecipientUser { get; private set; } = null!;
    public DateTime EscalatedAtUtc { get; private set; }

    private ApprovalWorkflowEscalation() { }

    public ApprovalWorkflowEscalation(
        long workflowStageId,
        int escalationNumber,
        int recipientUserId,
        DateTime escalatedAtUtc)
    {
        if (workflowStageId <= 0 ||
            escalationNumber <= 0 ||
            recipientUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workflowStageId));
        }

        if (escalatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Escalation time must be UTC.",
                nameof(escalatedAtUtc));
        }

        WorkflowStageId = workflowStageId;
        EscalationNumber = escalationNumber;
        RecipientUserId = recipientUserId;
        EscalatedAtUtc = escalatedAtUtc;
    }
}
