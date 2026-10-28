using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public sealed record WorkflowStageResolution(
    int Sequence,
    int ApproverUserId,
    int? DeadlineMinutes,
    int? EscalationAfterMinutes,
    int? EscalationApproverUserId);

public enum WorkflowDecisionResult
{
    Success,
    NotWorkflowRequest,
    NotAssigned,
    Conflict,
    AttachmentsNotClean
}

public sealed record WorkflowEscalationRequest(
    long StageId,
    int RequestId,
    int StageSequence,
    int RecipientUserId,
    int EscalationNumber);
