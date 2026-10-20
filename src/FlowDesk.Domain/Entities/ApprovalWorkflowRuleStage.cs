using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public sealed class ApprovalWorkflowRuleStage
{
    public int Id { get; private set; }
    public int WorkflowRuleId { get; private set; }
    public ApprovalWorkflowRule WorkflowRule { get; private set; } = null!;
    public int Sequence { get; private set; }
    public WorkflowApproverType ApproverType { get; private set; }
    public int? ApproverDepartmentId { get; private set; }
    public Department? ApproverDepartment { get; private set; }
    public int? SpecificApproverUserId { get; private set; }
    public User? SpecificApproverUser { get; private set; }
    public int? DeadlineMinutes { get; private set; }
    public int? EscalationAfterMinutes { get; private set; }
    public int? EscalationApproverUserId { get; private set; }
    public User? EscalationApproverUser { get; private set; }

    private ApprovalWorkflowRuleStage() { }

    public ApprovalWorkflowRuleStage(
        int sequence,
        WorkflowApproverType approverType,
        int? approverDepartmentId,
        int? specificApproverUserId,
        int? deadlineMinutes,
        int? escalationAfterMinutes,
        int? escalationApproverUserId)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (deadlineMinutes is <= 0 ||
            escalationAfterMinutes is <= 0 ||
            (escalationAfterMinutes.HasValue &&
             (!deadlineMinutes.HasValue ||
              escalationAfterMinutes.Value < deadlineMinutes.Value)))
        {
            throw new ArgumentException(
                "Escalation must occur after its positive deadline.");
        }

        Sequence = sequence;
        ApproverType = approverType;
        ApproverDepartmentId = approverDepartmentId;
        SpecificApproverUserId = specificApproverUserId;
        DeadlineMinutes = deadlineMinutes;
        EscalationAfterMinutes = escalationAfterMinutes;
        EscalationApproverUserId = escalationApproverUserId;
    }
}
