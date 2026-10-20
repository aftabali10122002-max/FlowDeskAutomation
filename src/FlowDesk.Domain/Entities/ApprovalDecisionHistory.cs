using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public sealed class ApprovalDecisionHistory
{
    public long Id { get; private set; }
    public long WorkflowStageId { get; private set; }
    public ApprovalWorkflowStage WorkflowStage { get; private set; } = null!;
    public int ManagerUserId { get; private set; }
    public User ManagerUser { get; private set; } = null!;
    public ApprovalWorkflowStageStatus Decision { get; private set; }
    public string? Comment { get; private set; }
    public DateTime DecidedAtUtc { get; private set; }

    private ApprovalDecisionHistory() { }

    public ApprovalDecisionHistory(
        long workflowStageId,
        int managerUserId,
        ApprovalWorkflowStageStatus decision,
        string? comment,
        DateTime decidedAtUtc)
    {
        if (workflowStageId <= 0 || managerUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workflowStageId));
        }

        if (decision is not ApprovalWorkflowStageStatus.Approved and
            not ApprovalWorkflowStageStatus.Rejected)
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }

        var normalizedComment = string.IsNullOrWhiteSpace(comment)
            ? null
            : comment.Trim();
        if (normalizedComment?.Length > 2000 ||
            (decision == ApprovalWorkflowStageStatus.Rejected &&
             normalizedComment is null))
        {
            throw new ArgumentException(
                "A rejection reason of at most 2000 characters is required.",
                nameof(comment));
        }

        if (decidedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Decision time must be UTC.",
                nameof(decidedAtUtc));
        }

        WorkflowStageId = workflowStageId;
        ManagerUserId = managerUserId;
        Decision = decision;
        Comment = normalizedComment;
        DecidedAtUtc = decidedAtUtc;
    }
}
