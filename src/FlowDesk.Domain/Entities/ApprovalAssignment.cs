using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public class ApprovalAssignment
{
    public long Id { get; private set; }

    public int RequestId { get; private set; }

    public ApprovalRequest Request { get; private set; } = null!;

    public int ApproverUserId { get; private set; }

    public User ApproverUser { get; private set; } = null!;

    public long? WorkflowStageId { get; private set; }

    public ApprovalWorkflowStage? WorkflowStage { get; private set; }

    public ApprovalAssignmentStatus Status { get; private set; }
        = ApprovalAssignmentStatus.Pending;

    public DateTime AssignedAtUtc { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    private ApprovalAssignment() { }

    public ApprovalAssignment(
        int requestId,
        int approverUserId,
        long? workflowStageId = null)
    {
        if (requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        if (approverUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(approverUserId));
        }

        RequestId = requestId;
        ApproverUserId = approverUserId;
        WorkflowStageId = workflowStageId;
        AssignedAtUtc = DateTime.UtcNow;
    }

    public void MarkApproved(DateTime decidedAtUtc)
    {
        Decide(ApprovalAssignmentStatus.Approved, decidedAtUtc);
    }

    public void MarkRejected(DateTime decidedAtUtc)
    {
        Decide(ApprovalAssignmentStatus.Rejected, decidedAtUtc);
    }

    public void MarkCancelled(DateTime decidedAtUtc)
    {
        Decide(ApprovalAssignmentStatus.Cancelled, decidedAtUtc);
    }

    private void Decide(
        ApprovalAssignmentStatus status,
        DateTime decidedAtUtc)
    {
        if (Status != ApprovalAssignmentStatus.Pending)
        {
            throw new InvalidOperationException(
                "Only pending assignments can be decided.");
        }

        if (decidedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Decision time must be UTC.",
                nameof(decidedAtUtc));
        }

        Status = status;
        DecidedAtUtc = decidedAtUtc;
    }
}
