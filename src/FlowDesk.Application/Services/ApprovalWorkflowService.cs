using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.Services;

public sealed class ApprovalWorkflowService(
    IApprovalWorkflowRepository workflowRepository,
    IApprovalRequestRepository requestRepository,
    IUserRepository userRepository,
    IDepartmentRepository departmentRepository)
{
    public async Task<List<ApprovalWorkflowRule>> GetRulesAsync(int adminUserId)
    {
        await RequireRoleAsync(adminUserId, UserRoles.Admin);
        return await workflowRepository.GetRulesAsync();
    }

    public async Task<ApprovalWorkflowRule?> GetRuleAsync(
        int adminUserId,
        int ruleId)
    {
        await RequireRoleAsync(adminUserId, UserRoles.Admin);
        return await workflowRepository.GetRuleAsync(ruleId);
    }

    public async Task<ApprovalWorkflowRule> CreateRuleAsync(
        int adminUserId,
        WorkflowRuleInputDto input)
    {
        await RequireRoleAsync(adminUserId, UserRoles.Admin);
        var rule = new ApprovalWorkflowRule(
            input.WorkflowKey,
            input.Name,
            1,
            input.DepartmentId,
            input.MinimumAmount,
            input.MaximumAmountExclusive,
            input.Priority,
            BuildStages(input.Stages));
        await ValidateRuleReferencesAsync(rule);
        await workflowRepository.AddRuleAsync(rule);
        await workflowRepository.SaveChangesAsync();
        return rule;
    }

    public async Task<ApprovalWorkflowRule?> UpdateDraftAsync(
        int adminUserId,
        int ruleId,
        WorkflowRuleInputDto input)
    {
        await RequireRoleAsync(adminUserId, UserRoles.Admin);
        var rule = await workflowRepository.GetRuleAsync(ruleId, tracked: true);
        if (rule == null)
        {
            return null;
        }

        var stages = BuildStages(input.Stages);
        rule.UpdateDraft(
            input.WorkflowKey,
            input.Name,
            input.DepartmentId,
            input.MinimumAmount,
            input.MaximumAmountExclusive,
            input.Priority,
            stages);
        await ValidateRuleReferencesAsync(rule);
        await workflowRepository.SaveChangesAsync();
        return rule;
    }

    public async Task<ApprovalWorkflowRule?> CreateVersionAsync(
        int adminUserId,
        int ruleId)
    {
        await RequireRoleAsync(adminUserId, UserRoles.Admin);
        var source = await workflowRepository.GetRuleAsync(ruleId);
        if (source == null)
        {
            return null;
        }

        var versions = (await workflowRepository.GetRulesAsync())
            .Where(x => x.WorkflowKey == source.WorkflowKey)
            .Select(x => x.Version);
        var rule = new ApprovalWorkflowRule(
            source.WorkflowKey,
            source.Name,
            versions.DefaultIfEmpty(0).Max() + 1,
            source.DepartmentId,
            source.MinimumAmount,
            source.MaximumAmountExclusive,
            source.Priority,
            source.Stages.Select(CloneStage));
        await workflowRepository.AddRuleAsync(rule);
        await workflowRepository.SaveChangesAsync();
        return rule;
    }

    public async Task ActivateRuleAsync(int adminUserId, int ruleId)
    {
        await RequireRoleAsync(adminUserId, UserRoles.Admin);
        var rule = await workflowRepository.GetRuleAsync(ruleId);
        if (rule == null)
        {
            throw new KeyNotFoundException("Workflow rule not found.");
        }

        await ValidateRuleReferencesAsync(rule);
        await workflowRepository.ActivateRuleAsync(ruleId, ConflictsWith);
    }

    public async Task DeactivateRuleAsync(int adminUserId, int ruleId)
    {
        await RequireRoleAsync(adminUserId, UserRoles.Admin);
        await workflowRepository.DeactivateRuleAsync(ruleId);
    }

    public async Task<ApprovalRequest> CreateRequestAsync(
        ApprovalRequest request,
        RequestPriority priority)
    {
        var activeRules = await workflowRepository.GetActiveRulesAsync();
        var matches = activeRules
            .Where(rule => Matches(rule, request, priority))
            .ToList();

        request.SetPriority(priority);
        if (matches.Count == 0)
        {
            await requestRepository.AddAsync(request);
            await requestRepository.SaveChangesAsync();
        }
        else if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                "More than one active workflow matches this request. Contact an administrator.");
        }
        else
        {
            var rule = matches[0];
            await ValidateRuleReferencesAsync(rule);
            var stageResolutions = await ResolveStagesAsync(rule, request);
            request = await workflowRepository.CreateRoutedRequestAsync(
                request,
                rule.Id,
                stageResolutions);
        }

        if (request.CreatedByUserId is int ownerId)
        {
            await workflowRepository.CreateNotificationAsync(
                ownerId,
                request.Id,
                WorkflowNotificationType.RequestSubmitted,
                $"Your request REQ-{request.Id:D4} was submitted.",
                $"request-submitted:{request.Id}",
                DateTime.UtcNow);
        }

        return request;
    }

    public async Task<WorkflowDecisionResult> DecideAsync(
        int requestId,
        int managerUserId,
        bool approve,
        string? comment)
    {
        await RequireRoleAsync(managerUserId, UserRoles.Manager);
        if (!await workflowRepository.HasWorkflowAsync(requestId))
        {
            return WorkflowDecisionResult.NotWorkflowRequest;
        }

        var manager = await userRepository.GetByIdAsync(managerUserId);
        if (manager is not { IsActive: true })
        {
            throw new UnauthorizedAccessException(
                "Only an active Manager can decide workflow stages.");
        }

        var normalizedComment = string.IsNullOrWhiteSpace(comment)
            ? null
            : comment.Trim();
        if (normalizedComment?.Length > 2000)
        {
            throw new ArgumentException(
                "Comments cannot exceed 2000 characters.",
                nameof(comment));
        }

        if (!approve && normalizedComment is null)
        {
            throw new ArgumentException(
                "A rejection reason is required.",
                nameof(comment));
        }

        return await workflowRepository.DecideWorkflowStageAsync(
            requestId,
            managerUserId,
            approve,
            normalizedComment);
    }

    public async Task<ApprovalWorkflowInstance?> GetForUserAsync(
        int requestId,
        int userId,
        string role)
    {
        await RequireRoleAsync(userId, role);
        var request = role switch
        {
            UserRoles.Employee =>
                await requestRepository.GetOwnedByIdAsync(requestId, userId),
            UserRoles.Manager =>
                await requestRepository.GetAssignedToManagerByIdAsync(
                    requestId,
                    userId),
            UserRoles.Admin =>
                await requestRepository.GetAdminVisibleByIdAsync(requestId),
            _ => null
        };

        if (request == null)
        {
            return null;
        }

        return await workflowRepository.GetWorkflowForRequestAsync(requestId);
    }

    public async Task<RequestCancellationResult?> CancelIfWorkflowAsync(
        int requestId,
        int ownerUserId)
    {
        await RequireRoleAsync(ownerUserId, UserRoles.Employee);
        return await workflowRepository.HasWorkflowAsync(requestId)
            ? await workflowRepository.CancelWorkflowAsync(
                requestId,
                ownerUserId)
            : null;
    }

    public async Task<int> ProcessEscalationsAsync(int adminUserId)
    {
        await RequireRoleAsync(adminUserId, UserRoles.Admin);
        return await workflowRepository.ProcessOverdueEscalationsAsync(
            DateTime.UtcNow);
    }

    public async Task<WorkflowNotificationPage> GetNotificationsAsync(
        int userId,
        int page = 1,
        int pageSize = 20)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                "Page must be positive and page size must be between 1 and 100.");
        }

        var user = await userRepository.GetByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedAccessException(
                "The authenticated user is not active.");
        }

        return await workflowRepository.GetNotificationsAsync(
            userId,
            page,
            pageSize);
    }

    public async Task<WorkflowNotification> CreateNotificationAsync(
        int recipientUserId,
        int? requestId,
        WorkflowNotificationType type,
        string message,
        string deduplicationKey,
        string? recipientEmail = null,
        string? emailSubject = null,
        string? emailBody = null)
    {
        var recipient = await userRepository.GetByIdAsync(recipientUserId);
        if (recipient is not { IsActive: true })
        {
            throw new InvalidOperationException(
                "The notification recipient is unavailable or inactive.");
        }

        return await workflowRepository.CreateNotificationAsync(
            recipientUserId,
            requestId,
            type,
            message,
            deduplicationKey,
            DateTime.UtcNow,
            recipientEmail,
            emailSubject,
            emailBody);
    }

    public async Task<bool> MarkNotificationReadAsync(
        int userId,
        long notificationId)
    {
        var user = await userRepository.GetByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedAccessException(
                "The authenticated user is not active.");
        }

        return await workflowRepository.MarkNotificationReadAsync(
            notificationId,
            userId,
            DateTime.UtcNow);
    }

    private async Task ValidateRuleReferencesAsync(ApprovalWorkflowRule rule)
    {
        var departments = await departmentRepository.GetAllAsync();
        var activeDepartments = departments.Where(x => x.IsActive).ToList();
        if (rule.DepartmentId.HasValue &&
            !activeDepartments.Any(x => x.Id == rule.DepartmentId.Value))
        {
            throw new ArgumentException(
                "Workflow department must exist and be active.");
        }

        var users = await userRepository.GetAllAsync();
        var managers = users
            .Where(x => x.Role == UserRoles.Manager && x.IsActive)
            .ToList();

        foreach (var stage in rule.Stages)
        {
            if (!Enum.IsDefined(stage.ApproverType))
            {
                throw new ArgumentException(
                    "Workflow stage has an unsupported approver type.");
            }

            if (stage.ApproverDepartmentId.HasValue &&
                !activeDepartments.Any(x =>
                    x.Id == stage.ApproverDepartmentId.Value))
            {
                throw new ArgumentException(
                    "Stage department must exist and be active.");
            }

            if (stage.ApproverType == WorkflowApproverType.SpecificManager)
            {
                RequireManager(
                    managers,
                    stage.SpecificApproverUserId,
                    "Stage approver must be an active Manager.");
            }
            else if (stage.SpecificApproverUserId.HasValue)
            {
                throw new ArgumentException(
                    "Only a specific-Manager stage can specify a user.");
            }

            if (stage.ApproverType == WorkflowApproverType.DepartmentManager &&
                !stage.ApproverDepartmentId.HasValue)
            {
                throw new ArgumentException(
                    "A department-Manager stage requires a department.");
            }

            if (stage.ApproverType != WorkflowApproverType.DepartmentManager &&
                stage.ApproverDepartmentId.HasValue)
            {
                throw new ArgumentException(
                    "Only a department-Manager stage can specify a department.");
            }

            if (stage.ApproverType ==
                WorkflowApproverType.RequestDepartmentManager)
            {
                var scopeDepartments = rule.DepartmentId.HasValue
                    ? activeDepartments.Where(x =>
                        x.Id == rule.DepartmentId.Value).ToList()
                    : activeDepartments;
                if (scopeDepartments.Count == 0 ||
                    scopeDepartments.Any(department =>
                        managers.Count(manager =>
                            manager.DepartmentId == department.Id) != 1))
                {
                    throw new ArgumentException(
                        "Each department in this rule's scope must have exactly one active Manager for request-department approval.");
                }
            }

            if (stage.ApproverType ==
                WorkflowApproverType.DepartmentManager &&
                managers.Count(manager =>
                    manager.DepartmentId == stage.ApproverDepartmentId) != 1)
            {
                throw new ArgumentException(
                    "The configured department must have exactly one active Manager.");
            }

            if (stage.EscalationApproverUserId.HasValue)
            {
                RequireManager(
                    managers,
                    stage.EscalationApproverUserId,
                    "Escalation recipient must be an active Manager.");
            }
        }
    }

    private async Task<List<WorkflowStageResolution>> ResolveStagesAsync(
        ApprovalWorkflowRule rule,
        ApprovalRequest request)
    {
        var users = await userRepository.GetAllAsync();
        var managers = users
            .Where(x => x.Role == UserRoles.Manager && x.IsActive)
            .ToList();
        var result = new List<WorkflowStageResolution>();

        foreach (var stage in rule.Stages.OrderBy(x => x.Sequence))
        {
            var departmentId = stage.ApproverType switch
            {
                WorkflowApproverType.RequestDepartmentManager =>
                    request.DepartmentId,
                WorkflowApproverType.DepartmentManager =>
                    stage.ApproverDepartmentId,
                _ => null
            };

            var approverId = stage.ApproverType switch
            {
                WorkflowApproverType.SpecificManager =>
                    RequireManager(
                        managers,
                        stage.SpecificApproverUserId,
                        "The configured workflow Manager is inactive or unavailable.")
                        .Id,
                WorkflowApproverType.RequestDepartmentManager =>
                    RequireUniqueDepartmentManager(managers, departmentId).Id,
                WorkflowApproverType.DepartmentManager =>
                    RequireUniqueDepartmentManager(managers, departmentId).Id,
                _ => throw new ArgumentException(
                    "Workflow stage has an unsupported approver type.")
            };

            result.Add(new WorkflowStageResolution(
                stage.Sequence,
                approverId,
                stage.DeadlineMinutes,
                stage.EscalationAfterMinutes,
                stage.EscalationApproverUserId));
        }

        return result;
    }

    private async Task<User> RequireRoleAsync(int userId, string role)
    {
        var user = userId > 0
            ? await userRepository.GetByIdAsync(userId)
            : null;
        if (user == null ||
            !user.IsActive ||
            !string.Equals(user.Role, role, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The authenticated user is not authorized for this operation.");
        }

        return user;
    }

    private static List<ApprovalWorkflowRuleStage> BuildStages(
        IEnumerable<WorkflowRuleStageDto> stageDtos)
    {
        return stageDtos.Select(stage =>
            new ApprovalWorkflowRuleStage(
                stage.Sequence,
                stage.ApproverType,
                stage.ApproverDepartmentId,
                stage.SpecificApproverUserId,
                stage.DeadlineMinutes,
                stage.EscalationAfterMinutes,
                stage.EscalationApproverUserId)).ToList();
    }

    private static ApprovalWorkflowRuleStage CloneStage(
        ApprovalWorkflowRuleStage stage)
    {
        return new ApprovalWorkflowRuleStage(
            stage.Sequence,
            stage.ApproverType,
            stage.ApproverDepartmentId,
            stage.SpecificApproverUserId,
            stage.DeadlineMinutes,
            stage.EscalationAfterMinutes,
            stage.EscalationApproverUserId);
    }

    private static User RequireManager(
        IReadOnlyCollection<User> managers,
        int? userId,
        string message)
    {
        var manager = managers.FirstOrDefault(x => x.Id == userId);
        return manager ?? throw new ArgumentException(message);
    }

    private static User RequireUniqueDepartmentManager(
        IReadOnlyCollection<User> managers,
        int? departmentId)
    {
        if (!departmentId.HasValue)
        {
            throw new ArgumentException(
                "A department-specific stage requires a department.");
        }

        var matches = managers.Where(x => x.DepartmentId == departmentId).ToList();
        return matches.Count == 1
            ? matches[0]
            : throw new ArgumentException(
                matches.Count == 0
                    ? "No active Manager exists in the configured department."
                    : "More than one active Manager exists in the configured department; choose a specific Manager.");
    }

    private static bool Matches(
        ApprovalWorkflowRule rule,
        ApprovalRequest request,
        RequestPriority priority)
    {
        return rule.Status == ApprovalWorkflowRuleStatus.Active &&
            (!rule.DepartmentId.HasValue ||
             rule.DepartmentId == request.DepartmentId) &&
            rule.MinimumAmount <= request.Amount &&
            (!rule.MaximumAmountExclusive.HasValue ||
             request.Amount < rule.MaximumAmountExclusive.Value) &&
            (!rule.Priority.HasValue || rule.Priority == priority);
    }

    private static bool ConflictsWith(
        ApprovalWorkflowRule candidate,
        IReadOnlyList<ApprovalWorkflowRule> activeRules)
    {
        return activeRules.Any(existing =>
            existing.Id != candidate.Id &&
            DepartmentsOverlap(existing.DepartmentId, candidate.DepartmentId) &&
            PrioritiesOverlap(existing.Priority, candidate.Priority) &&
            AmountsOverlap(existing, candidate));
    }

    private static bool DepartmentsOverlap(int? left, int? right) =>
        !left.HasValue || !right.HasValue || left == right;

    private static bool PrioritiesOverlap(
        RequestPriority? left,
        RequestPriority? right) =>
        !left.HasValue || !right.HasValue || left == right;

    private static bool AmountsOverlap(
        ApprovalWorkflowRule left,
        ApprovalWorkflowRule right) =>
        (!left.MaximumAmountExclusive.HasValue ||
         left.MaximumAmountExclusive.Value > right.MinimumAmount) &&
        (!right.MaximumAmountExclusive.HasValue ||
         right.MaximumAmountExclusive.Value > left.MinimumAmount);
}
