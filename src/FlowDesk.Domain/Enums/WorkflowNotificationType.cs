namespace FlowDesk.Domain.Enums;

public enum WorkflowNotificationType
{
    RequestSubmitted = 1,
    RequestAssigned = 2,
    StageAdvanced = 3,
    RequestApproved = 4,
    RequestRejected = 5,
    RequestCancelled = 6,
    DeadlineReminder = 7,
    Escalation = 8,
    RequestNeedsAdminAssignment = 9
}
