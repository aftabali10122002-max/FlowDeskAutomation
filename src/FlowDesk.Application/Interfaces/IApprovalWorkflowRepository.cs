using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.Interfaces;

public interface IApprovalWorkflowRepository
{
    Task<List<ApprovalWorkflowRule>> GetRulesAsync();

    Task<List<ApprovalWorkflowRule>> GetActiveRulesAsync();

    Task<ApprovalWorkflowRule?> GetRuleAsync(int id, bool tracked = false);

    Task AddRuleAsync(ApprovalWorkflowRule rule, int actorUserId);

    Task SaveRuleUpdateAsync(ApprovalWorkflowRule rule, int actorUserId);

    Task SaveChangesAsync();

    Task ActivateRuleAsync(
        int actorUserId,
        int id,
        Func<ApprovalWorkflowRule, IReadOnlyList<ApprovalWorkflowRule>, bool>
            hasConflict);

    Task DeactivateRuleAsync(int actorUserId, int id);

    Task<ApprovalRequest> CreateRoutedRequestAsync(
        ApprovalRequest request,
        int workflowRuleId,
        IReadOnlyList<WorkflowStageResolution> stages);

    Task<ApprovalRequest> CreateUnroutedRequestAsync(
        ApprovalRequest request);

    Task<AssignmentCreationResult> CreateManualAssignmentAsync(
        int requestId,
        int approverUserId,
        int actorUserId);

    Task<ApprovalActionResult> DecideLegacyAssignmentAsync(
        int requestId,
        int managerUserId,
        bool approve);

    Task<RequestCancellationResult> CancelUnroutedRequestAsync(
        int requestId,
        int ownerUserId);

    Task<bool> HasWorkflowAsync(int requestId);

    Task<ApprovalWorkflowInstance?> GetWorkflowForRequestAsync(
        int requestId);

    Task<WorkflowDecisionResult> DecideWorkflowStageAsync(
        int requestId,
        int managerUserId,
        bool approve,
        string? comment);

    Task<RequestCancellationResult> CancelWorkflowAsync(
        int requestId,
        int ownerUserId);

    Task<int> ProcessOverdueEscalationsAsync(DateTime nowUtc);

    Task<int> ProcessDeadlineRemindersAsync(
        DateTime nowUtc,
        int reminderMinutesBeforeDeadline);

    Task<WorkflowNotification> CreateNotificationAsync(
        int recipientUserId,
        int? requestId,
        WorkflowNotificationType type,
        string message,
        string deduplicationKey,
        DateTime createdAtUtc,
        string? recipientEmail = null,
        string? emailSubject = null,
        string? emailBody = null);

    Task<WorkflowNotificationPage> GetNotificationsAsync(
        int userId,
        int page,
        int pageSize);

    Task<bool> MarkNotificationReadAsync(
        long notificationId,
        int userId,
        DateTime readAtUtc);

    Task<List<WorkflowEmailOutboxMessage>> GetPendingEmailsAsync(
        DateTime nowUtc,
        int batchSize);

    Task<bool> TryLeaseEmailAsync(
        long id,
        DateTime nowUtc,
        DateTime leaseUntilUtc);

    Task CompleteEmailAttemptAsync(
        long id,
        DateTime attemptedAtUtc,
        bool sent);
}
