using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Services;

public class ApprovalRequestService
{
    private const string EmployeeRole = "Employee";
    private const string ManagerRole = "Manager";
    private const string AdminRole = "Admin";

    private readonly IApprovalRequestRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly ApprovalWorkflowService _workflowService;

    public ApprovalRequestService(
        IApprovalRequestRepository repository,
        IUserRepository userRepository,
        IDepartmentRepository departmentRepository,
        ApprovalWorkflowService workflowService)
    {
        _repository = repository;
        _userRepository = userRepository;
        _departmentRepository = departmentRepository;
        _workflowService = workflowService;
    }

    public async Task<List<ApprovalRequest>> GetForEmployeeAsync(int userId)
    {
        await RequireRoleAsync(userId, EmployeeRole);
        return await _repository.GetForEmployeeAsync(userId);
    }

    public async Task<List<ApprovalRequest>> GetAssignedToManagerAsync(int userId)
    {
        await RequireRoleAsync(userId, ManagerRole);
        return await _repository.GetAssignedToManagerAsync(userId);
    }

    public async Task<List<ApprovalRequest>> GetAllOwnedForAdminAsync(int adminUserId)
    {
        await RequireRoleAsync(adminUserId, AdminRole);
        return await _repository.GetAllOwnedAsync();
    }

    public async Task<List<ApprovalRequest>> GetLegacyForAdminAsync(
        int adminUserId)
    {
        await RequireRoleAsync(adminUserId, AdminRole);
        return await _repository.GetLegacyAsync();
    }

    public async Task<ApprovalRequest?> GetForEmployeeAsync(int requestId, int userId)
    {
        await RequireRoleAsync(userId, EmployeeRole);
        return await _repository.GetOwnedByIdAsync(requestId, userId);
    }

    public async Task<ApprovalRequest?> GetAssignedToManagerAsync(
        int requestId,
        int managerUserId)
    {
        await RequireRoleAsync(managerUserId, ManagerRole);
        return await _repository.GetAssignedToManagerByIdAsync(
            requestId,
            managerUserId);
    }

    public async Task<ApprovalRequest?> GetForAdminAsync(
        int requestId,
        int adminUserId)
    {
        await RequireRoleAsync(adminUserId, AdminRole);
        return await _repository.GetAdminVisibleByIdAsync(requestId);
    }

    public async Task<ApprovalRequest?> GetLegacyForAdminAsync(
        int requestId,
        int adminUserId)
    {
        await RequireRoleAsync(adminUserId, AdminRole);
        return await _repository.GetLegacyByIdAsync(requestId);
    }

    public async Task<ApprovalRequest> CreateAsync(
        string title,
        decimal amount,
        int ownerUserId,
        FlowDesk.Domain.Enums.RequestPriority? priority = null)
    {
        var owner = await RequireRoleAsync(ownerUserId, EmployeeRole);
        var request = new ApprovalRequest(
            title,
            amount,
            owner.Id,
            owner.DepartmentId);

        return await _workflowService.CreateRequestAsync(
            request,
            priority ?? FlowDesk.Domain.Enums.RequestPriority.Medium);
    }

    public async Task<ApprovalActionResult> ApproveAsync(
        int requestId,
        int managerUserId,
        string? comment = null)
    {
        await RequireRoleAsync(managerUserId, ManagerRole);
        var workflowResult = await _workflowService.DecideAsync(
            requestId,
            managerUserId,
            approve: true,
            comment);
        if (workflowResult !=
            FlowDesk.Application.Interfaces.WorkflowDecisionResult.NotWorkflowRequest)
        {
            return workflowResult switch
            {
                FlowDesk.Application.Interfaces.WorkflowDecisionResult.Success =>
                    ApprovalActionResult.Success,
                FlowDesk.Application.Interfaces.WorkflowDecisionResult.NotAssigned =>
                    ApprovalActionResult.NotAssigned,
                _ => ApprovalActionResult.Conflict
            };
        }

        var request = await _repository.GetByIdAsync(requestId);
        var result = await _repository.DecideAssignedRequestAsync(
            requestId,
            managerUserId,
            approve: true);
        if (result == ApprovalActionResult.Success &&
            request?.CreatedByUserId is int ownerId)
        {
            await NotifyOwnerOfLegacyDecisionAsync(
                request,
                ownerId,
                approved: true);
        }

        return result;
    }

    public async Task<ApprovalActionResult> RejectAsync(
        int requestId,
        int managerUserId,
        string? reason = null)
    {
        await RequireRoleAsync(managerUserId, ManagerRole);
        var workflowResult = await _workflowService.DecideAsync(
            requestId,
            managerUserId,
            approve: false,
            reason);
        if (workflowResult !=
            FlowDesk.Application.Interfaces.WorkflowDecisionResult.NotWorkflowRequest)
        {
            return workflowResult switch
            {
                FlowDesk.Application.Interfaces.WorkflowDecisionResult.Success =>
                    ApprovalActionResult.Success,
                FlowDesk.Application.Interfaces.WorkflowDecisionResult.NotAssigned =>
                    ApprovalActionResult.NotAssigned,
                _ => ApprovalActionResult.Conflict
            };
        }

        var request = await _repository.GetByIdAsync(requestId);
        var result = await _repository.DecideAssignedRequestAsync(
            requestId,
            managerUserId,
            approve: false);
        if (result == ApprovalActionResult.Success &&
            request?.CreatedByUserId is int ownerId)
        {
            await NotifyOwnerOfLegacyDecisionAsync(
                request,
                ownerId,
                approved: false);
        }

        return result;
    }

    public async Task<RequestCancellationResult> CancelAsync(
        int requestId,
        int ownerUserId)
    {
        await RequireRoleAsync(ownerUserId, EmployeeRole);
        var workflowResult = await _workflowService.CancelIfWorkflowAsync(
            requestId,
            ownerUserId);
        if (workflowResult.HasValue)
        {
            return workflowResult.Value;
        }

        var request = await _repository.GetOwnedByIdAsync(
            requestId,
            ownerUserId);
        var result = await _repository.CancelOwnedRequestAsync(
            requestId,
            ownerUserId);
        if (result == RequestCancellationResult.Success && request != null)
        {
            foreach (var assignment in request.Assignments.Where(
                         x => x.Status == FlowDesk.Domain.Enums.ApprovalAssignmentStatus.Pending))
            {
                await _workflowService.CreateNotificationAsync(
                    assignment.ApproverUserId,
                    requestId,
                    FlowDesk.Domain.Enums.WorkflowNotificationType.RequestCancelled,
                    $"Request REQ-{requestId:D4} was cancelled by its requester.",
                    $"cancel:{requestId}:{assignment.Id}");
            }
        }

        return result;
    }

    public async Task<AssignmentCreationResult> AssignToManagerAsync(
        int requestId,
        int approverUserId,
        int adminUserId)
    {
        await RequireRoleAsync(adminUserId, AdminRole);

        if (approverUserId <= 0)
        {
            return AssignmentCreationResult.InvalidApprover;
        }

        var manager = await _userRepository.GetByIdAsync(approverUserId);
        if (manager == null ||
            !string.Equals(manager.Role, ManagerRole, StringComparison.Ordinal))
        {
            return AssignmentCreationResult.InvalidApprover;
        }

        var request = await _repository.GetByIdAsync(requestId);
        if (request == null)
        {
            return AssignmentCreationResult.RequestNotFound;
        }

        if (!request.DepartmentId.HasValue ||
            manager.DepartmentId != request.DepartmentId.Value)
        {
            return AssignmentCreationResult.InvalidApprover;
        }

        var department = await _departmentRepository.GetByIdAsync(
            request.DepartmentId.Value);
        if (department == null || !department.IsActive)
        {
            return AssignmentCreationResult.InvalidApprover;
        }

        var result = await _repository.TryAddPendingAssignmentAsync(
            requestId,
            new ApprovalAssignment(requestId, approverUserId));
        if (result == AssignmentCreationResult.Created)
        {
            var assignedRequest = await _repository.GetAssignedToManagerByIdAsync(
                requestId,
                approverUserId);
            var assignment = assignedRequest?.Assignments
                .Where(x => x.Status == FlowDesk.Domain.Enums.ApprovalAssignmentStatus.Pending)
                .OrderByDescending(x => x.AssignedAtUtc)
                .FirstOrDefault();
            if (assignment != null)
            {
                await _workflowService.CreateNotificationAsync(
                    approverUserId,
                    requestId,
                    FlowDesk.Domain.Enums.WorkflowNotificationType.RequestAssigned,
                    $"Request REQ-{requestId:D4} has been assigned to you for approval.",
                    $"assignment:{assignment.Id}",
                    manager.Email,
                    $"Approval assigned: {request.Title}",
                    $"Request REQ-{requestId:D4} ({request.Title}) has been assigned to you for approval.");
            }
        }

        return result;
    }

    private async Task NotifyOwnerOfLegacyDecisionAsync(
        ApprovalRequest request,
        int ownerId,
        bool approved)
    {
        var owner = await _userRepository.GetByIdAsync(ownerId);
        if (owner == null || !owner.IsActive)
        {
            return;
        }

        var type = approved
            ? FlowDesk.Domain.Enums.WorkflowNotificationType.RequestApproved
            : FlowDesk.Domain.Enums.WorkflowNotificationType.RequestRejected;
        var message = approved
            ? $"Your request REQ-{request.Id:D4} was approved."
            : $"Your request REQ-{request.Id:D4} was rejected.";
        await _workflowService.CreateNotificationAsync(
            ownerId,
            request.Id,
            type,
            message,
            $"legacy-decision:{request.Id}:{(approved ? "approved" : "rejected")}",
            owner.Email,
            $"Request decision: {request.Title}",
            message);
    }

    private async Task<User> RequireRoleAsync(int userId, string role)
    {
        var user = userId > 0
            ? await _userRepository.GetByIdAsync(userId)
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
}
