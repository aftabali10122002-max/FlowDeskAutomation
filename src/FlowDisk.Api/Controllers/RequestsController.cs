using System.Security.Claims;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Application.Services;
using FlowDesk.Domain.Entities;
using FlowDisk.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FlowDisk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/requests")]
public class RequestsController : ControllerBase
{
    private const string EmployeeRole = "Employee";
    private const string ManagerRole = "Manager";
    private const string AdminRole = "Admin";

    private readonly ApprovalRequestService _service;
    private readonly ApprovalWorkflowService _workflowService;
    private readonly IAuthorizationService _authorizationService;

    public RequestsController(
        ApprovalRequestService service,
        ApprovalWorkflowService workflowService,
        IAuthorizationService authorizationService)
    {
        _service = service;
        _workflowService = workflowService;
        _authorizationService = authorizationService;
    }

    private static RequestResponseDto MapToDto(
        ApprovalRequest request,
        int? assignmentViewerUserId = null,
        bool includeAllAssignments = false,
        bool includeIdentityEmails = false)
    {
        return new RequestResponseDto
        {
            Id = request.Id,
            Title = request.Title,
            Description = request.Description,
            Amount = request.Amount,
            RequestTypeId = request.RequestTypeId,
            RequestTypeCode = request.RequestType?.Code,
            RequestTypeName = request.RequestType?.DisplayName,
            RequestDetails = MapRequestDetails(request),
            Priority = request.Priority,
            Status = request.Status.ToString(),
            CreatedAtUtc = request.CreatedAtUtc,
            CreatedByUserId = request.CreatedByUserId,
            CreatedByName = request.CreatedByUser?.FullName,
            CreatedByEmail = includeIdentityEmails
                ? request.CreatedByUser?.Email
                : null,
            SubmittedBy = request.CreatedByUser?.FullName,
            DepartmentId = request.DepartmentId,
            DepartmentName = request.Department?.Name,
            Department = request.Department?.Name,
            Assignments = request.Assignments
                .Where(assignment =>
                    includeAllAssignments ||
                    (assignmentViewerUserId.HasValue &&
                     assignment.ApproverUserId == assignmentViewerUserId.Value))
                .Select(assignment => new ApprovalAssignmentResponseDto
                {
                    Id = assignment.Id,
                    ApproverName = assignment.ApproverUser.FullName,
                    ApproverEmail = includeIdentityEmails
                        ? assignment.ApproverUser.Email
                        : null,
                    Status = assignment.Status.ToString(),
                    AssignedAtUtc = assignment.AssignedAtUtc,
                    DecidedAtUtc = assignment.DecidedAtUtc
                })
                .ToList()
        };
    }

    private static object? MapRequestDetails(ApprovalRequest request)
    {
        if (request.MaterialPurchaseDetails is { } material)
        {
            return new
            {
                itemName = material.ItemName,
                quantity = material.Quantity,
                businessJustification = material.BusinessJustification
            };
        }

        if (request.LeaveDetails is { } leave)
        {
            return new
            {
                leaveType = leave.LeaveType,
                startDate = leave.StartDate,
                endDate = leave.EndDate,
                reason = leave.Reason,
                totalLeaveDays = leave.TotalLeaveDays,
                calculatedLeaveUnits = leave.CalculatedLeaveUnits,
                cancellationOfRequestId = leave.CancellationOfRequestId
            };
        }

        if (request.ExpenseReimbursementDetails is { } expense)
        {
            return new
            {
                category = expense.Category,
                expenseDate = expense.ExpenseDate,
                businessJustification = expense.BusinessJustification
            };
        }

        if (request.ItAccessDetails is { } itAccess)
        {
            return new
            {
                systemApplication = itAccess.SystemApplication,
                requestedAccessLevel = itAccess.RequestedAccessLevel,
                durationDays = itAccess.DurationDays,
                businessJustification = itAccess.BusinessJustification
            };
        }

        if (request.TravelDetails is { } travel)
        {
            return new
            {
                destination = travel.Destination,
                startDate = travel.StartDate,
                endDate = travel.EndDate,
                businessPurpose = travel.BusinessPurpose
            };
        }

        if (request.WorkFromHomeDetails is { } workFromHome)
        {
            return new
            {
                startDate = workFromHome.StartDate,
                endDate = workFromHome.EndDate,
                reason = workFromHome.Reason
            };
        }

        return null;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = GetAuthenticatedUserId();
        if (userId == null)
        {
            return Forbid();
        }

        List<ApprovalRequest> requests;
        if (User.IsInRole(AdminRole))
        {
            if (!await IsAuthorizedForPolicyAsync(
                    RequestPolicies.ReadAllRequests))
            {
                return Forbid();
            }

            var ownedRequests = await _service.GetAllOwnedForAdminAsync(
                userId.Value);
            var legacyRequests = await _service.GetLegacyForAdminAsync(
                userId.Value);
            requests = ownedRequests
                .Concat(legacyRequests)
                .OrderByDescending(request => request.CreatedAtUtc)
                .ToList();
        }
        else if (User.IsInRole(ManagerRole))
        {
            requests = await _service.GetAssignedToManagerAsync(userId.Value);
        }
        else if (User.IsInRole(EmployeeRole))
        {
            requests = await _service.GetForEmployeeAsync(userId.Value);
        }
        else
        {
            return Forbid();
        }

        var managerUserId = User.IsInRole(ManagerRole) ? userId : null;
        var includeAllAssignments = User.IsInRole(AdminRole);
        var includeIdentityEmails = User.IsInRole(AdminRole);
        return Ok(requests
            .Select(request => MapToDto(
                request,
                managerUserId,
                includeAllAssignments,
                includeIdentityEmails))
            .ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var userId = GetAuthenticatedUserId();
        if (userId == null)
        {
            return Forbid();
        }

        ApprovalRequest? request;
        if (User.IsInRole(AdminRole))
        {
            if (!await IsAuthorizedForPolicyAsync(
                    RequestPolicies.ReadAllRequests))
            {
                return Forbid();
            }

            request = await _service.GetForAdminAsync(id, userId.Value);
            request ??= await _service.GetLegacyForAdminAsync(
                id,
                userId.Value);
        }
        else if (User.IsInRole(ManagerRole))
        {
            request = await _service.GetAssignedToManagerAsync(
                id,
                userId.Value);
        }
        else if (User.IsInRole(EmployeeRole))
        {
            request = await _service.GetForEmployeeAsync(id, userId.Value);
        }
        else
        {
            return Forbid();
        }

        return request == null
            ? NotFound(new { message = "Request not found." })
            : Ok(MapToDto(
                request,
                User.IsInRole(ManagerRole) ? userId : null,
                includeAllAssignments: User.IsInRole(AdminRole),
                includeIdentityEmails:
                    User.IsInRole(AdminRole) ||
                    request.CreatedByUserId == userId));
    }

    [HttpGet("{id:int}/workflow")]
    public async Task<IActionResult> GetWorkflow(int id)
    {
        var userId = GetAuthenticatedUserId();
        if (userId == null)
        {
            return Forbid();
        }

        var role = User.IsInRole(AdminRole)
            ? AdminRole
            : User.IsInRole(ManagerRole)
                ? ManagerRole
                : User.IsInRole(EmployeeRole)
                    ? EmployeeRole
                    : null;
        if (role == null)
        {
            return Forbid();
        }

        var workflow = await _workflowService.GetForUserAsync(
            id,
            userId.Value,
            role);
        return workflow == null
            ? NotFound(new { message = "Workflow not found." })
            : Ok(MapWorkflow(workflow));
    }

    [HttpGet("legacy")]
    [Authorize(Policy = RequestPolicies.ReviewLegacyRequests)]
    public async Task<IActionResult> GetLegacyRequests()
    {
        var userId = GetAuthenticatedUserId();
        if (userId == null)
        {
            return Forbid();
        }

        var requests = await _service.GetLegacyForAdminAsync(userId.Value);
        return Ok(requests.Select(request => MapToDto(request)).ToList());
    }

    [HttpGet("legacy/{id:int}")]
    [Authorize(Policy = RequestPolicies.ReviewLegacyRequests)]
    public async Task<IActionResult> GetLegacyRequest(int id)
    {
        var userId = GetAuthenticatedUserId();
        if (userId == null)
        {
            return Forbid();
        }

        var request = await _service.GetLegacyForAdminAsync(id, userId.Value);
        return request == null
            ? NotFound(new { message = "Request not found." })
            : Ok(MapToDto(request));
    }

    [HttpPost]
    [Authorize(Roles = EmployeeRole)]
    public async Task<IActionResult> Create(CreateRequestDto dto)
    {
        var userId = GetAuthenticatedUserId();
        if (userId == null)
        {
            return Forbid();
        }

        ApprovalRequest request;
        try
        {
            if (string.IsNullOrWhiteSpace(dto.RequestTypeCode))
            {
                if (string.IsNullOrWhiteSpace(dto.Title))
                {
                    return BadRequest(new { message = "Title is required." });
                }

                if (dto.Amount is null or <= 0)
                {
                    return BadRequest(new
                    {
                        message = "Amount must be greater than zero."
                    });
                }

                request = await _service.CreateAsync(
                    dto.Title,
                    dto.Amount.Value,
                    userId.Value,
                    dto.Priority,
                    dto.Description);
            }
            else
            {
                request = await _service.CreateTypedAsync(dto, userId.Value);
            }
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = request.Id },
            MapToDto(request));
    }

    [HttpPost("{id:int}/assignments")]
    [Authorize(Policy = RequestPolicies.ManageApprovalAssignments)]
    public async Task<IActionResult> Assign(int id, AssignApprovalRequestDto dto)
    {
        var adminUserId = GetAuthenticatedUserId();
        if (adminUserId == null)
        {
            return Forbid();
        }

        var result = await _service.AssignToManagerAsync(
            id,
            dto.ApproverUserId,
            adminUserId.Value);

        return result switch
        {
            AssignmentCreationResult.Created => StatusCode(
                StatusCodes.Status201Created,
                new { message = "Approval assignment created." }),
            AssignmentCreationResult.RequestNotFound => NotFound(
                new { message = "Request not found." }),
            AssignmentCreationResult.InvalidApprover => BadRequest(
                new
                {
                    message = "Approver must be an active Manager in the request's department."
                }),
            AssignmentCreationResult.Conflict => Conflict(
                new
                {
                    message = "Request cannot accept another active assignment."
                }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPatch("{id:int}/approve")]
    [Authorize(Roles = ManagerRole)]
    public async Task<IActionResult> Approve(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        WorkflowDecisionRequestDto? dto)
    {
        var userId = GetAuthenticatedUserId();
        if (userId == null)
        {
            return Forbid();
        }

        return MapDecisionResult(
            await _service.ApproveAsync(id, userId.Value, dto?.Comment),
            "Request approved successfully.");
    }

    [HttpPatch("{id:int}/reject")]
    [Authorize(Roles = ManagerRole)]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        WorkflowDecisionRequestDto? dto)
    {
        var userId = GetAuthenticatedUserId();
        if (userId == null)
        {
            return Forbid();
        }

        return MapDecisionResult(
            await _service.RejectAsync(id, userId.Value, dto?.Comment),
            "Request rejected successfully.");
    }

    [HttpPatch("{id:int}/cancel")]
    [Authorize(Roles = EmployeeRole)]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = GetAuthenticatedUserId();
        if (userId == null)
        {
            return Forbid();
        }

        var result = await _service.CancelAsync(id, userId.Value);
        return result switch
        {
            RequestCancellationResult.Success => Ok(new
            {
                message = "Request cancelled successfully."
            }),
            RequestCancellationResult.NotFound => NotFound(
                new { message = "Request not found." }),
            RequestCancellationResult.Conflict => Conflict(
                new { message = "Only pending requests can be cancelled." }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPost("{id:int}/cancellation-requests")]
    [Authorize(Roles = EmployeeRole)]
    public async Task<IActionResult> CreateLeaveCancellationRequest(
        int id,
        [FromBody] LeaveCancellationInput input)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Forbid();
        }

        var cancellation = await _service.CreateLeaveCancellationAsync(
            id,
            userId.Value,
            input);
        return CreatedAtAction(
            nameof(GetById),
            new { id = cancellation.Id },
            MapToDto(cancellation));
    }

    private int? GetAuthenticatedUserId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claimValue, out var userId) && userId > 0
            ? userId
            : null;
    }

    private async Task<bool> IsAuthorizedForPolicyAsync(string policyName)
    {
        var result = await _authorizationService.AuthorizeAsync(
            User,
            policyName);
        return result.Succeeded;
    }

    private IActionResult MapDecisionResult(
        ApprovalActionResult result,
        string successMessage)
    {
        return result switch
        {
            ApprovalActionResult.Success => Ok(new
            {
                message = successMessage
            }),
            ApprovalActionResult.NotAssigned => NotFound(
                new { message = "Request not found." }),
            ApprovalActionResult.Conflict => Conflict(
                new { message = "Request is no longer pending." }),
            ApprovalActionResult.AttachmentsNotClean => Conflict(
                new
                {
                    message = "Approval is blocked until every request attachment has completed a clean malware scan."
                }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static WorkflowInstanceResponseDto MapWorkflow(
        ApprovalWorkflowInstance workflow)
    {
        return new WorkflowInstanceResponseDto
        {
            RequestId = workflow.RequestId,
            WorkflowName = workflow.WorkflowRule.Name,
            WorkflowVersion = workflow.RuleVersion,
            Status = workflow.Status.ToString(),
            StartedAtUtc = AsUtc(workflow.StartedAtUtc),
            CompletedAtUtc = AsUtc(workflow.CompletedAtUtc),
            Stages = workflow.Stages
                .OrderBy(stage => stage.Sequence)
                .Select(stage => new WorkflowStageResponseDto
                {
                    Id = stage.Id,
                    Sequence = stage.Sequence,
                    Status = stage.Status.ToString(),
                    ApproverUserId = stage.ApproverUserId,
                    ApproverName = stage.ApproverUser.FullName,
                    DeadlineMinutes = stage.DeadlineMinutes,
                    ActivatedAtUtc = AsUtc(stage.ActivatedAtUtc),
                    DueAtUtc = AsUtc(stage.DueAtUtc),
                    DecidedAtUtc = AsUtc(stage.DecidedAtUtc),
                    Decision = stage.DecisionHistory?.Decision.ToString(),
                    Comment = stage.DecisionHistory?.Comment,
                    EscalatedAtUtc = stage.Escalations
                        .OrderByDescending(escalation => escalation.EscalatedAtUtc)
                        .Select(escalation => (DateTime?)escalation.EscalatedAtUtc)
                        .FirstOrDefault() is { } escalatedAtUtc
                            ? DateTime.SpecifyKind(
                                escalatedAtUtc,
                                DateTimeKind.Utc)
                            : null
                })
                .ToList()
        };
    }

    private static DateTime AsUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) =>
        value.HasValue ? AsUtc(value.Value) : null;
}
