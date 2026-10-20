using System.Data;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlowDesk.Infrastructure.Repositories;

public sealed class ApprovalWorkflowRepository : IApprovalWorkflowRepository
{
    private readonly AppDbContext context;
    private readonly IWorkflowNotificationPublisher publisher;
    private readonly ILogger<ApprovalWorkflowRepository> logger;
    private readonly bool emailEnabled;

    public ApprovalWorkflowRepository(
        AppDbContext context,
        IWorkflowNotificationPublisher publisher,
        IConfiguration configuration,
        ILogger<ApprovalWorkflowRepository> logger)
    {
        this.context = context;
        this.publisher = publisher;
        this.logger = logger;
        emailEnabled = bool.TryParse(
            configuration["Email:Enabled"],
            out var configuredEmailEnabled) &&
            configuredEmailEnabled;
    }

    public Task<List<ApprovalWorkflowRule>> GetRulesAsync()
    {
        return context.ApprovalWorkflowRules
            .AsNoTracking()
            .Include(x => x.Department)
            .Include(x => x.Stages)
            .OrderBy(x => x.WorkflowKey)
            .ThenBy(x => x.Version)
            .ToListAsync();
    }

    public Task<List<ApprovalWorkflowRule>> GetActiveRulesAsync()
    {
        return context.ApprovalWorkflowRules
            .AsNoTracking()
            .Include(x => x.Stages)
            .Where(x => x.Status == ApprovalWorkflowRuleStatus.Active)
            .ToListAsync();
    }

    public Task<ApprovalWorkflowRule?> GetRuleAsync(
        int id,
        bool tracked = false)
    {
        var query = context.ApprovalWorkflowRules
            .Include(x => x.Department)
            .Include(x => x.Stages)
            .Where(x => x.Id == id);
        return tracked
            ? query.FirstOrDefaultAsync()
            : query.AsNoTracking().FirstOrDefaultAsync();
    }

    public async Task AddRuleAsync(ApprovalWorkflowRule rule)
    {
        await context.ApprovalWorkflowRules.AddAsync(rule);
    }

    public Task SaveChangesAsync() => context.SaveChangesAsync();

    public async Task ActivateRuleAsync(
        int id,
        Func<ApprovalWorkflowRule, IReadOnlyList<ApprovalWorkflowRule>, bool>
            hasConflict)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var candidate = await context.ApprovalWorkflowRules
            .Include(x => x.Stages)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (candidate == null)
        {
            throw new KeyNotFoundException("Workflow rule not found.");
        }

        var activeRules = await context.ApprovalWorkflowRules
            .AsNoTracking()
            .Where(x => x.Status == ApprovalWorkflowRuleStatus.Active)
            .ToListAsync();
        if (hasConflict(candidate, activeRules))
        {
            throw new InvalidOperationException(
                "This rule overlaps another active workflow rule.");
        }

        candidate.Activate(DateTime.UtcNow);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task DeactivateRuleAsync(int id)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var rule = await context.ApprovalWorkflowRules
            .SingleOrDefaultAsync(x => x.Id == id);
        if (rule == null)
        {
            throw new KeyNotFoundException("Workflow rule not found.");
        }

        rule.Deactivate();
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<ApprovalRequest> CreateRoutedRequestAsync(
        ApprovalRequest request,
        int workflowRuleId,
        IReadOnlyList<WorkflowStageResolution> resolutions)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var rule = await context.ApprovalWorkflowRules
            .Include(x => x.Stages)
            .SingleOrDefaultAsync(x =>
                x.Id == workflowRuleId &&
                x.Status == ApprovalWorkflowRuleStatus.Active);
        if (rule == null)
        {
            throw new InvalidOperationException(
                "The selected workflow rule is no longer active.");
        }

        var ruleStages = rule.Stages.OrderBy(x => x.Sequence).ToList();
        if (resolutions.Count != ruleStages.Count)
        {
            throw new InvalidOperationException(
                "The selected workflow stage configuration changed.");
        }

        var approverIds = resolutions
            .Select(x => x.ApproverUserId)
            .Concat(resolutions
                .Where(x => x.EscalationApproverUserId.HasValue)
                .Select(x => x.EscalationApproverUserId!.Value))
            .Distinct()
            .ToArray();
        var managers = await context.Users
            .Where(x => approverIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);
        foreach (var resolution in resolutions)
        {
            if (!managers.TryGetValue(resolution.ApproverUserId, out var manager) ||
                manager.Role != UserRoles.Manager ||
                !manager.IsActive)
            {
                throw new InvalidOperationException(
                    "A configured workflow approver is no longer an active Manager.");
            }

            var ruleStage = ruleStages.Single(x =>
                x.Sequence == resolution.Sequence);
            var requiredDepartmentId = ruleStage.ApproverType switch
            {
                WorkflowApproverType.RequestDepartmentManager =>
                    request.DepartmentId,
                WorkflowApproverType.DepartmentManager =>
                    ruleStage.ApproverDepartmentId,
                _ => null
            };
            if (requiredDepartmentId.HasValue &&
                manager.DepartmentId != requiredDepartmentId.Value)
            {
                throw new InvalidOperationException(
                    "A workflow approver no longer belongs to the configured department.");
            }

            if (resolution.EscalationApproverUserId is int escalationUserId &&
                (!managers.TryGetValue(escalationUserId, out var escalationManager) ||
                 escalationManager.Role != UserRoles.Manager ||
                 !escalationManager.IsActive))
            {
                throw new InvalidOperationException(
                    "A configured escalation recipient is no longer an active Manager.");
            }
        }

        context.ApprovalRequests.Add(request);
        await context.SaveChangesAsync();

        var nowUtc = DateTime.UtcNow;
        var workflowStages = resolutions
            .OrderBy(x => x.Sequence)
            .Select(x => new ApprovalWorkflowStage(
                x.Sequence,
                x.ApproverUserId,
                x.DeadlineMinutes,
                x.EscalationAfterMinutes,
                x.EscalationApproverUserId))
            .ToList();
        workflowStages[0].Activate(nowUtc);
        var instance = new ApprovalWorkflowInstance(
            request.Id,
            rule,
            workflowStages,
            nowUtc);
        context.ApprovalWorkflowInstances.Add(instance);
        await context.SaveChangesAsync();

        context.ApprovalAssignments.Add(new ApprovalAssignment(
            request.Id,
            workflowStages[0].ApproverUserId,
            workflowStages[0].Id));
        await context.SaveChangesAsync();
        var assignmentNotification = await AddNotificationAndEmailAsync(
            workflowStages[0].ApproverUserId,
            request.Id,
            WorkflowNotificationType.RequestAssigned,
            $"Request REQ-{request.Id:D4} has been assigned to you for approval.",
            $"assignment:{workflowStages[0].Id}",
            nowUtc,
            managers[workflowStages[0].ApproverUserId].Email,
            $"Approval assigned: {request.Title}",
            $"Request REQ-{request.Id:D4} ({request.Title}) has been assigned to you for approval.");
        await transaction.CommitAsync();
        await PublishAfterCommitAsync([assignmentNotification]);
        return request;
    }

    public Task<bool> HasWorkflowAsync(int requestId)
    {
        return context.ApprovalWorkflowInstances
            .AnyAsync(x => x.RequestId == requestId);
    }

    public Task<ApprovalWorkflowInstance?> GetWorkflowForRequestAsync(
        int requestId)
    {
        return context.ApprovalWorkflowInstances
            .AsNoTracking()
            .Include(x => x.WorkflowRule)
            .Include(x => x.Stages)
                .ThenInclude(x => x.ApproverUser)
            .Include(x => x.Stages)
                .ThenInclude(x => x.DecisionHistory)
            .Include(x => x.Stages)
                .ThenInclude(x => x.Escalations)
            .SingleOrDefaultAsync(x => x.RequestId == requestId);
    }

    public async Task<WorkflowDecisionResult> DecideWorkflowStageAsync(
        int requestId,
        int managerUserId,
        bool approve,
        string? comment)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var instance = await context.ApprovalWorkflowInstances
            .Include(x => x.WorkflowRule)
            .Include(x => x.Request)
            .Include(x => x.Stages)
                .ThenInclude(x => x.ApproverUser)
            .SingleOrDefaultAsync(x => x.RequestId == requestId);
        var createdNotifications = new List<WorkflowNotification>();
        if (instance == null)
        {
            return WorkflowDecisionResult.NotWorkflowRequest;
        }

        if (instance.Status != ApprovalWorkflowStatus.Active ||
            instance.Request.Status != RequestStatus.Pending)
        {
            return WorkflowDecisionResult.Conflict;
        }

        var managerIsEligible = await context.Users.AnyAsync(x =>
            x.Id == managerUserId &&
            x.Role == UserRoles.Manager &&
            x.IsActive);
        if (!managerIsEligible)
        {
            return WorkflowDecisionResult.NotAssigned;
        }

        var activeStage = instance.Stages.SingleOrDefault(
            x => x.Status == ApprovalWorkflowStageStatus.Active);
        if (activeStage == null ||
            activeStage.ApproverUserId != managerUserId)
        {
            return WorkflowDecisionResult.NotAssigned;
        }

        var nowUtc = DateTime.UtcNow;
        var stageDecision = approve
            ? ApprovalWorkflowStageStatus.Approved
            : ApprovalWorkflowStageStatus.Rejected;
        var stageRows = await context.ApprovalWorkflowStages
            .Where(x =>
                x.Id == activeStage.Id &&
                x.Status == ApprovalWorkflowStageStatus.Active &&
                x.ApproverUserId == managerUserId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, stageDecision)
                .SetProperty(x => x.DecidedAtUtc, nowUtc));
        if (stageRows != 1)
        {
            await transaction.RollbackAsync();
            return WorkflowDecisionResult.Conflict;
        }

        activeStage.Decide(stageDecision, nowUtc);
        var assignmentStatus = approve
            ? ApprovalAssignmentStatus.Approved
            : ApprovalAssignmentStatus.Rejected;
        var assignmentRows = await context.ApprovalAssignments
            .Where(x =>
                x.RequestId == requestId &&
                x.WorkflowStageId == activeStage.Id &&
                x.ApproverUserId == managerUserId &&
                x.Status == ApprovalAssignmentStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, assignmentStatus)
                .SetProperty(x => x.DecidedAtUtc, nowUtc));
        if (assignmentRows != 1)
        {
            await transaction.RollbackAsync();
            return WorkflowDecisionResult.Conflict;
        }

        context.ApprovalDecisionHistory.Add(new ApprovalDecisionHistory(
            activeStage.Id,
            managerUserId,
            stageDecision,
            comment,
            nowUtc));

        if (!approve)
        {
            var requestRows = await context.ApprovalRequests
                .Where(x =>
                    x.Id == requestId &&
                    x.Status == RequestStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, RequestStatus.Rejected));
            if (requestRows != 1)
            {
                await transaction.RollbackAsync();
                return WorkflowDecisionResult.Conflict;
            }

            foreach (var futureStage in instance.Stages.Where(
                         x => x.Status == ApprovalWorkflowStageStatus.Inactive))
            {
                futureStage.Cancel(nowUtc);
            }

            instance.Complete(ApprovalWorkflowStatus.Rejected, nowUtc);
        }
        else
        {
            var nextStage = instance.Stages
                .Where(x => x.Sequence > activeStage.Sequence)
                .OrderBy(x => x.Sequence)
                .FirstOrDefault();
            if (nextStage == null)
            {
                var requestRows = await context.ApprovalRequests
                    .Where(x =>
                        x.Id == requestId &&
                        x.Status == RequestStatus.Pending)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Status, RequestStatus.Approved));
                if (requestRows != 1)
                {
                    await transaction.RollbackAsync();
                    return WorkflowDecisionResult.Conflict;
                }

                instance.Complete(ApprovalWorkflowStatus.Approved, nowUtc);
            }
            else
            {
                nextStage.Activate(nowUtc);
            }
        }

        await context.SaveChangesAsync();
        if (approve &&
            instance.Status == ApprovalWorkflowStatus.Active)
        {
            var nextStage = instance.Stages.Single(
                x => x.Status == ApprovalWorkflowStageStatus.Active);
            context.ApprovalAssignments.Add(new ApprovalAssignment(
                requestId,
                nextStage.ApproverUserId,
                nextStage.Id));
            await context.SaveChangesAsync();
            createdNotifications.Add(await AddNotificationAndEmailAsync(
                nextStage.ApproverUserId,
                requestId,
                WorkflowNotificationType.StageAdvanced,
                $"Request REQ-{requestId:D4} has advanced to stage {nextStage.Sequence} and is awaiting your approval.",
                $"assignment:{nextStage.Id}",
                nowUtc,
                nextStage.ApproverUser.Email,
                $"Approval assigned: {instance.Request.Title}",
                $"Request REQ-{requestId:D4} ({instance.Request.Title}) has advanced to stage {nextStage.Sequence} and is awaiting your approval."));
        }

        if (instance.Request.CreatedByUserId is int requestOwnerId)
        {
            var notificationType = !approve
                ? WorkflowNotificationType.RequestRejected
                : instance.Status == ApprovalWorkflowStatus.Approved
                    ? WorkflowNotificationType.RequestApproved
                    : WorkflowNotificationType.StageAdvanced;
            var message = notificationType switch
            {
                WorkflowNotificationType.RequestRejected =>
                    $"Your request REQ-{requestId:D4} was rejected.",
                WorkflowNotificationType.RequestApproved =>
                    $"Your request REQ-{requestId:D4} was approved.",
                _ =>
                    $"Your request REQ-{requestId:D4} advanced to the next approval stage."
            };
            createdNotifications.Add(await AddNotificationAndEmailAsync(
                requestOwnerId,
                requestId,
                notificationType,
                message,
                $"decision:{activeStage.Id}:owner",
                nowUtc));
        }

        await transaction.CommitAsync();
        await PublishAfterCommitAsync(createdNotifications);
        return WorkflowDecisionResult.Success;
    }

    public async Task<RequestCancellationResult> CancelWorkflowAsync(
        int requestId,
        int ownerUserId)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var request = await context.ApprovalRequests
            .SingleOrDefaultAsync(x =>
                x.Id == requestId &&
                x.CreatedByUserId == ownerUserId);
        if (request == null)
        {
            var owned = await context.ApprovalRequests.AnyAsync(x =>
                x.Id == requestId &&
                x.CreatedByUserId == ownerUserId);
            return owned
                ? RequestCancellationResult.Conflict
                : RequestCancellationResult.NotFound;
        }

        if (request.Status != RequestStatus.Pending)
        {
            return RequestCancellationResult.Conflict;
        }

        var nowUtc = DateTime.UtcNow;
        var requestRows = await context.ApprovalRequests
            .Where(x =>
                x.Id == requestId &&
                x.CreatedByUserId == ownerUserId &&
                x.Status == RequestStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, RequestStatus.Cancelled));
        if (requestRows != 1)
        {
            await transaction.RollbackAsync();
            return RequestCancellationResult.Conflict;
        }

        var instance = await context.ApprovalWorkflowInstances
            .Include(x => x.Stages)
            .SingleAsync(x => x.RequestId == requestId);
        var pendingApproverIds = await context.ApprovalAssignments
            .Where(x =>
                x.RequestId == requestId &&
                x.Status == ApprovalAssignmentStatus.Pending)
            .Select(x => x.ApproverUserId)
            .Distinct()
            .ToListAsync();
        await context.ApprovalAssignments
            .Where(x =>
                x.RequestId == requestId &&
                x.Status == ApprovalAssignmentStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ApprovalAssignmentStatus.Cancelled)
                .SetProperty(x => x.DecidedAtUtc, nowUtc));
        foreach (var stage in instance.Stages)
        {
            stage.Cancel(nowUtc);
        }

        instance.Complete(ApprovalWorkflowStatus.Cancelled, nowUtc);
        await context.SaveChangesAsync();
        var approvers = await context.Users
            .Where(x => pendingApproverIds.Contains(x.Id))
            .ToListAsync();
        var notifications = new List<WorkflowNotification>();
        foreach (var approver in approvers)
        {
            notifications.Add(await AddNotificationAndEmailAsync(
                approver.Id,
                requestId,
                WorkflowNotificationType.RequestCancelled,
                $"Request REQ-{requestId:D4} was cancelled by its requester.",
                $"cancel:{requestId}:{approver.Id}",
                nowUtc));
        }

        await transaction.CommitAsync();
        await PublishAfterCommitAsync(notifications);
        return RequestCancellationResult.Success;
    }

    public async Task<int> ProcessOverdueEscalationsAsync(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Escalation time must be UTC.");
        }

        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var candidates = await context.ApprovalWorkflowStages
            .Include(x => x.WorkflowInstance)
                .ThenInclude(x => x.Request)
            .Include(x => x.Escalations)
            .Where(x =>
                x.Status == ApprovalWorkflowStageStatus.Active &&
                x.ActivatedAtUtc.HasValue &&
                x.EscalationAfterMinutes.HasValue &&
                x.EscalationApproverUserId.HasValue &&
                !x.Escalations.Any())
            .OrderBy(x => x.ActivatedAtUtc)
            .Take(200)
            .ToListAsync();

        var ready = candidates.Where(x =>
                nowUtc - x.ActivatedAtUtc!.Value >=
                TimeSpan.FromMinutes(x.EscalationAfterMinutes!.Value))
            .ToList();
        var recipientIds = ready
            .Select(x => x.EscalationApproverUserId!.Value)
            .Distinct()
            .ToArray();
        var activeRecipients = await context.Users
            .Where(x =>
                recipientIds.Contains(x.Id) &&
                x.Role == UserRoles.Manager &&
                x.IsActive)
            .ToDictionaryAsync(x => x.Id);

        var notifications = new List<WorkflowNotification>();
        foreach (var stage in ready.Where(
                     x => activeRecipients.ContainsKey(
                         x.EscalationApproverUserId!.Value)))
        {
            var escalation = new ApprovalWorkflowEscalation(
                stage.Id,
                escalationNumber: 1,
                stage.EscalationApproverUserId!.Value,
                nowUtc);
            context.ApprovalWorkflowEscalations.Add(escalation);
            await context.SaveChangesAsync();
            notifications.Add(await AddNotificationAndEmailAsync(
                stage.EscalationApproverUserId.Value,
                stage.WorkflowInstance.RequestId,
                WorkflowNotificationType.Escalation,
                $"Request REQ-{stage.WorkflowInstance.RequestId:D4}, stage {stage.Sequence}, is overdue and needs attention.",
                $"escalation-stage:{stage.Id}:1",
                nowUtc,
                recipientEmail: activeRecipients[
                    stage.EscalationApproverUserId.Value].Email,
                emailSubject: $"Escalated approval: request REQ-{stage.WorkflowInstance.RequestId:D4}",
                emailBody: $"Request REQ-{stage.WorkflowInstance.RequestId:D4}, stage {stage.Sequence}, is overdue and needs attention.",
                escalationId: escalation.Id));
        }

        var processed = ready.Count(x =>
            activeRecipients.ContainsKey(x.EscalationApproverUserId!.Value));
        await transaction.CommitAsync();
        await PublishAfterCommitAsync(notifications);
        return processed;
    }

    public async Task<int> ProcessDeadlineRemindersAsync(
        DateTime nowUtc,
        int reminderMinutesBeforeDeadline)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        if (reminderMinutesBeforeDeadline <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reminderMinutesBeforeDeadline));
        }

        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var reminderWindowEnd = nowUtc.AddMinutes(
            reminderMinutesBeforeDeadline);
        var candidates = await context.ApprovalWorkflowStages
            .Include(x => x.WorkflowInstance)
                .ThenInclude(x => x.Request)
            .Where(x =>
                x.Status == ApprovalWorkflowStageStatus.Active &&
                x.DueAtUtc > nowUtc &&
                x.DueAtUtc <= reminderWindowEnd &&
                x.WorkflowInstance.Status == ApprovalWorkflowStatus.Active &&
                x.WorkflowInstance.Request.Status == RequestStatus.Pending &&
                !context.WorkflowNotifications.Any(n =>
                    n.DeduplicationKey == $"deadline-reminder:{x.Id}"))
            .OrderBy(x => x.DueAtUtc)
            .Take(200)
            .ToListAsync();

        var approverIds = candidates.Select(x => x.ApproverUserId)
            .Distinct()
            .ToArray();
        var activeApprovers = await context.Users
            .Where(x =>
                approverIds.Contains(x.Id) &&
                x.Role == UserRoles.Manager &&
                x.IsActive)
            .ToListAsync();
        var approverLookup = activeApprovers.ToDictionary(x => x.Id);
        var notifications = new List<WorkflowNotification>();
        foreach (var stage in candidates)
        {
            if (!approverLookup.ContainsKey(stage.ApproverUserId))
            {
                continue;
            }

            notifications.Add(await AddNotificationAndEmailAsync(
                stage.ApproverUserId,
                stage.WorkflowInstance.RequestId,
                WorkflowNotificationType.DeadlineReminder,
                $"Request REQ-{stage.WorkflowInstance.RequestId:D4}, stage {stage.Sequence}, is approaching its approval deadline.",
                $"deadline-reminder:{stage.Id}",
                nowUtc));
        }

        await transaction.CommitAsync();
        await PublishAfterCommitAsync(notifications);
        return notifications.Count;
    }

    public async Task<WorkflowNotification> CreateNotificationAsync(
        int recipientUserId,
        int? requestId,
        WorkflowNotificationType type,
        string message,
        string deduplicationKey,
        DateTime createdAtUtc,
        string? recipientEmail = null,
        string? emailSubject = null,
        string? emailBody = null)
    {
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var notification = await AddNotificationAndEmailAsync(
            recipientUserId,
            requestId,
            type,
            message,
            deduplicationKey,
            createdAtUtc,
            recipientEmail: recipientEmail,
            emailSubject: emailSubject,
            emailBody: emailBody);
        await transaction.CommitAsync();
        await PublishAfterCommitAsync([notification]);
        return notification;
    }

    public async Task<WorkflowNotificationPage> GetNotificationsAsync(
        int userId,
        int page,
        int pageSize)
    {
        var notifications = context.WorkflowNotifications
            .AsNoTracking()
            .Where(x => x.RecipientUserId == userId)
            ;
        var totalCount = await notifications.CountAsync();
        var unreadCount = await notifications.CountAsync(x => x.ReadAtUtc == null);
        var items = await notifications
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return new WorkflowNotificationPage(
            items,
            totalCount,
            unreadCount,
            page,
            pageSize);
    }

    public async Task<bool> MarkNotificationReadAsync(
        long notificationId,
        int userId,
        DateTime readAtUtc)
    {
        var rows = await context.WorkflowNotifications
            .Where(x =>
                x.Id == notificationId &&
                x.RecipientUserId == userId &&
                x.ReadAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ReadAtUtc, readAtUtc));
        return rows == 1 ||
            await context.WorkflowNotifications.AnyAsync(x =>
                x.Id == notificationId &&
                x.RecipientUserId == userId &&
                x.ReadAtUtc != null);
    }

    public Task<List<WorkflowEmailOutboxMessage>> GetPendingEmailsAsync(
        DateTime nowUtc,
        int batchSize)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        return context.WorkflowEmailOutbox
            .AsNoTracking()
            .Where(x =>
                x.SentAtUtc == null &&
                (!x.LeaseUntilUtc.HasValue || x.LeaseUntilUtc <= nowUtc))
            .OrderBy(x => x.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync();
    }

    public async Task<bool> TryLeaseEmailAsync(
        long id,
        DateTime nowUtc,
        DateTime leaseUntilUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        EnsureUtc(leaseUntilUtc, nameof(leaseUntilUtc));
        var rows = await context.WorkflowEmailOutbox
            .Where(x =>
                x.Id == id &&
                x.SentAtUtc == null &&
                (!x.LeaseUntilUtc.HasValue || x.LeaseUntilUtc <= nowUtc))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LeaseUntilUtc, leaseUntilUtc));
        return rows == 1;
    }

    public async Task CompleteEmailAttemptAsync(
        long id,
        DateTime attemptedAtUtc,
        bool sent)
    {
        EnsureUtc(attemptedAtUtc, nameof(attemptedAtUtc));
        var message = await context.WorkflowEmailOutbox
            .SingleOrDefaultAsync(x => x.Id == id);
        if (message == null || message.SentAtUtc.HasValue)
        {
            return;
        }

        message.RecordAttempt(attemptedAtUtc);
        if (sent)
        {
            message.MarkSent(attemptedAtUtc);
        }
        else
        {
            message.ReleaseLease();
        }

        await context.SaveChangesAsync();
    }

    private async Task<WorkflowNotification> AddNotificationAndEmailAsync(
        int recipientUserId,
        int? requestId,
        WorkflowNotificationType type,
        string message,
        string deduplicationKey,
        DateTime createdAtUtc,
        string? recipientEmail = null,
        string? emailSubject = null,
        string? emailBody = null,
        long? escalationId = null)
    {
        var existing = await context.WorkflowNotifications
            .SingleOrDefaultAsync(x =>
                x.DeduplicationKey == deduplicationKey);
        if (existing != null)
        {
            return existing;
        }

        var notification = new WorkflowNotification(
            recipientUserId,
            requestId,
            escalationId,
            type,
            deduplicationKey,
            message,
            createdAtUtc);
        context.WorkflowNotifications.Add(notification);
        await context.SaveChangesAsync();

        if (emailEnabled &&
            !string.IsNullOrWhiteSpace(recipientEmail) &&
            !string.IsNullOrWhiteSpace(emailSubject) &&
            !string.IsNullOrWhiteSpace(emailBody))
        {
            context.WorkflowEmailOutbox.Add(new WorkflowEmailOutboxMessage(
                recipientUserId,
                notification.Id,
                recipientEmail,
                emailSubject,
                emailBody,
                deduplicationKey,
                createdAtUtc));
            await context.SaveChangesAsync();
        }

        return notification;
    }

    private async Task PublishAfterCommitAsync(
        IEnumerable<WorkflowNotification> notifications)
    {
        foreach (var notification in notifications)
        {
            try
            {
                await publisher.PublishAsync(notification);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Real-time delivery failed for notification {NotificationId}; it remains available through the notifications API.",
                    notification.Id);
            }
        }
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Timestamp must be UTC.",
                parameterName);
        }
    }
}
