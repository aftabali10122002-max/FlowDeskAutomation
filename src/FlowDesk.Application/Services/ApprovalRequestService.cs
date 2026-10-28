using FlowDesk.Application.Interfaces;
using FlowDesk.Application.DTOs;
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
    private readonly IRequestTypeRepository _requestTypeRepository;
    private readonly ApprovalWorkflowService _workflowService;

    public ApprovalRequestService(
        IApprovalRequestRepository repository,
        IUserRepository userRepository,
        IDepartmentRepository departmentRepository,
        IRequestTypeRepository requestTypeRepository,
        ApprovalWorkflowService workflowService)
    {
        _repository = repository;
        _userRepository = userRepository;
        _departmentRepository = departmentRepository;
        _requestTypeRepository = requestTypeRepository;
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
        FlowDesk.Domain.Enums.RequestPriority? priority = null,
        string? description = null)
    {
        var owner = await RequireRoleAsync(ownerUserId, EmployeeRole);
        var request = new ApprovalRequest(
            title,
            amount,
            owner.Id,
            owner.DepartmentId,
            description);

        return await _workflowService.CreateRequestAsync(
            request,
            priority ?? FlowDesk.Domain.Enums.RequestPriority.Medium);
    }

    public async Task<ApprovalRequest> CreateTypedAsync(
        CreateRequestDto dto,
        int ownerUserId,
        IReadOnlyCollection<PreparedRequestAttachment>? attachments = null)
    {
        var code = dto.RequestTypeCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
        {
            throw new ArgumentException("A request type is required.");
        }

        var requestType = await _requestTypeRepository
            .GetEnabledByCodeAsync(code);
        if (requestType is null)
        {
            throw new ArgumentException(
                "The selected request type is unavailable.");
        }

        EnsureExpectedPayload(dto, code);
        if (requestType.RequiresAmount != dto.Amount.HasValue)
        {
            throw new ArgumentException(requestType.RequiresAmount
                ? "An amount is required for this request type."
                : "Amount must be omitted for this request type.");
        }

        if (dto.Amount is <= 0)
        {
            throw new ArgumentException(
                "Amount must be greater than zero.");
        }

        var owner = await RequireRoleAsync(ownerUserId, EmployeeRole);
        var title = code switch
        {
            RequestTypeCodes.MaterialPurchase =>
                dto.MaterialPurchase!.ItemName,
            RequestTypeCodes.Leave =>
                $"{dto.Leave!.LeaveType.Trim()} leave request",
            RequestTypeCodes.ExpenseReimbursement =>
                $"{dto.ExpenseReimbursement!.Category.Trim()} reimbursement",
            RequestTypeCodes.ItAccess =>
                $"{dto.ItAccess!.SystemApplication.Trim()} access request",
            RequestTypeCodes.Travel =>
                $"{dto.Travel!.Destination.Trim()} travel request",
            RequestTypeCodes.WorkFromHome => "Work from home request",
            _ => throw new ArgumentException(
                "The selected request type is unsupported.")
        };

        var request = new ApprovalRequest(
            title,
            dto.Amount,
            owner.Id,
            owner.DepartmentId,
            dto.Description,
            requestType.RequiresAmount);
        switch (code)
        {
            case RequestTypeCodes.MaterialPurchase:
                request.SetMaterialPurchaseDetails(
                    requestType,
                    new MaterialPurchaseRequestDetails(
                        dto.MaterialPurchase!.ItemName,
                        dto.MaterialPurchase.Quantity
                            ?? throw new ArgumentException(
                                "Quantity is required."),
                        dto.MaterialPurchase.BusinessJustification));
                break;
            case RequestTypeCodes.Leave:
                request.SetLeaveDetails(
                    requestType,
                    new LeaveRequestDetails(
                        dto.Leave!.LeaveType,
                        dto.Leave.StartDate
                            ?? throw new ArgumentException(
                                "A leave start date is required."),
                        dto.Leave.EndDate
                            ?? throw new ArgumentException(
                                "A leave end date is required."),
                        dto.Leave.Reason,
                        dto.Leave.StartDayPortion,
                        dto.Leave.EndDayPortion));
                break;
            case RequestTypeCodes.ExpenseReimbursement:
                request.SetExpenseReimbursementDetails(
                    requestType,
                    new ExpenseReimbursementRequestDetails(
                        dto.ExpenseReimbursement!.Category,
                        dto.ExpenseReimbursement.ExpenseDate
                            ?? throw new ArgumentException(
                                "An expense date is required."),
                        dto.ExpenseReimbursement.BusinessJustification));
                break;
            case RequestTypeCodes.ItAccess:
                request.SetItAccessDetails(
                    requestType,
                    new ItAccessRequestDetails(
                        dto.ItAccess!.SystemApplication,
                        dto.ItAccess.RequestedAccessLevel,
                        dto.ItAccess.DurationDays,
                        dto.ItAccess.BusinessJustification));
                break;
            case RequestTypeCodes.Travel:
                request.SetTravelDetails(
                    requestType,
                    new TravelRequestDetails(
                        dto.Travel!.Destination,
                        dto.Travel.StartDate
                            ?? throw new ArgumentException(
                                "A travel start date is required."),
                        dto.Travel.EndDate
                            ?? throw new ArgumentException(
                                "A travel end date is required."),
                        dto.Travel.BusinessPurpose));
                break;
            case RequestTypeCodes.WorkFromHome:
                request.SetWorkFromHomeDetails(
                    requestType,
                    new WorkFromHomeRequestDetails(
                        dto.WorkFromHome!.StartDate
                            ?? throw new ArgumentException(
                                "A work-from-home start date is required."),
                        dto.WorkFromHome.EndDate
                            ?? throw new ArgumentException(
                                "A work-from-home end date is required."),
                        dto.WorkFromHome.Reason));
                break;
        }

        foreach (var prepared in attachments ?? [])
        {
            var attachment = new RequestAttachment(
                request,
                owner.Id,
                prepared.StorageKey,
                prepared.ClientUploadId,
                prepared.OriginalFileName,
                prepared.Extension,
                prepared.MediaType,
                prepared.SizeBytes,
                prepared.Sha256,
                DateTime.UtcNow);
            switch (prepared.ScanStatus)
            {
                case FlowDesk.Domain.Enums.AttachmentScanStatus.Clean:
                    attachment.MarkScanned(
                        isClean: true,
                        prepared.ScannerName
                            ?? throw new InvalidOperationException(
                                "A clean scan result must include a scanner name."),
                        prepared.ScannedAtUtc
                            ?? throw new InvalidOperationException(
                                "A clean scan result must include a scan time."));
                    break;
                case FlowDesk.Domain.Enums.AttachmentScanStatus.NotScanned:
                    attachment.MarkNotScanned(
                        prepared.ScannedAtUtc
                            ?? throw new InvalidOperationException(
                                "An unscanned result must include a completion time."));
                    break;
                case FlowDesk.Domain.Enums.AttachmentScanStatus.Rejected:
                    attachment.MarkScanned(
                        isClean: false,
                        prepared.ScannerName
                            ?? throw new InvalidOperationException(
                                "A rejected scan result must include a scanner name."),
                        prepared.ScannedAtUtc
                            ?? throw new InvalidOperationException(
                                "A rejected scan result must include a scan time."));
                    break;
                case FlowDesk.Domain.Enums.AttachmentScanStatus.Pending:
                    break;
                default:
                    throw new InvalidOperationException(
                        "The attachment scan status is invalid.");
            }

            request.AddAttachment(attachment);
        }

        return await _workflowService.CreateRequestAsync(
            request,
            dto.Priority ?? FlowDesk.Domain.Enums.RequestPriority.Medium);
    }

    public async Task<ApprovalRequest> CreateLeaveCancellationAsync(
        int originalRequestId,
        int ownerUserId,
        LeaveCancellationInput input)
    {
        var owner = await RequireRoleAsync(ownerUserId, EmployeeRole);
        if (originalRequestId <= 0 ||
            input is null ||
            string.IsNullOrWhiteSpace(input.Reason) ||
            input.Reason.Trim().Length > 2000)
        {
            throw new ArgumentException(
                "A valid original request and cancellation reason are required.");
        }

        var original = await _repository.GetOwnedByIdAsync(
            originalRequestId,
            ownerUserId);
        if (original?.LeaveDetails is not { } originalLeave ||
            original.Status != FlowDesk.Domain.Enums.RequestStatus.Approved)
        {
            throw new ArgumentException(
                "Only an approved leave request owned by the employee can be cancelled.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (originalLeave.StartDate <= today)
        {
            throw new ArgumentException(
                "Approved leave cancellation is available only before the leave starts.");
        }

        if (!await _repository.HasScheduledLeaveCommitmentAsync(
                originalRequestId))
        {
            throw new ArgumentException(
                "The approved leave does not have a scheduled balance commitment.");
        }

        if (await _repository.HasLeaveCancellationRequestAsync(originalRequestId))
        {
            throw new ArgumentException(
                "A cancellation approval request already exists for this leave.");
        }

        var requestType = original.RequestType ??
            await _requestTypeRepository.GetEnabledByCodeAsync(
                RequestTypeCodes.Leave);
        if (requestType is null || requestType.Code != RequestTypeCodes.Leave)
        {
            throw new ArgumentException(
                "The leave request type is unavailable.");
        }

        var cancellationDetails = new LeaveRequestDetails(
            originalLeave.LeaveType,
            originalLeave.StartDate,
            originalLeave.EndDate,
            input.Reason,
            originalLeave.StartDayPortion,
            originalLeave.EndDayPortion);
        cancellationDetails.MarkAsCancellationOf(originalRequestId);
        var cancellationRequest = new ApprovalRequest(
            $"Cancellation for REQ-{originalRequestId:D4}",
            amount: null,
            owner.Id,
            owner.DepartmentId,
            input.Reason,
            requireAmount: false);
        cancellationRequest.SetLeaveDetails(requestType, cancellationDetails);
        return await _workflowService.CreateRequestAsync(
            cancellationRequest,
            original.Priority ??
                FlowDesk.Domain.Enums.RequestPriority.Medium);
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
                FlowDesk.Application.Interfaces.WorkflowDecisionResult.AttachmentsNotClean =>
                    ApprovalActionResult.AttachmentsNotClean,
                _ => ApprovalActionResult.Conflict
            };
        }

        return await _workflowService.DecideLegacyAssignmentAsync(
            requestId,
            managerUserId,
            approve: true);
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

        return await _workflowService.DecideLegacyAssignmentAsync(
            requestId,
            managerUserId,
            approve: false);
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

        return await _workflowService.CancelUnroutedRequestAsync(
            requestId,
            ownerUserId);
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

        return await _workflowService.CreateManualAssignmentAsync(
            requestId,
            approverUserId,
            adminUserId);
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

    private static void EnsureExpectedPayload(
        CreateRequestDto dto,
        string requestTypeCode)
    {
        var supplied = new[]
        {
            dto.MaterialPurchase is not null,
            dto.Leave is not null,
            dto.ExpenseReimbursement is not null,
            dto.ItAccess is not null,
            dto.Travel is not null,
            dto.WorkFromHome is not null
        };
        var expectedIndex = requestTypeCode switch
        {
            RequestTypeCodes.MaterialPurchase => 0,
            RequestTypeCodes.Leave => 1,
            RequestTypeCodes.ExpenseReimbursement => 2,
            RequestTypeCodes.ItAccess => 3,
            RequestTypeCodes.Travel => 4,
            RequestTypeCodes.WorkFromHome => 5,
            _ => -1
        };
        if (expectedIndex < 0 ||
            supplied.Count(isSupplied => isSupplied) != 1 ||
            !supplied[expectedIndex])
        {
            throw new ArgumentException(
                "Supply exactly one detail payload matching the selected request type.");
        }
    }
}
