using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Repositories;

public sealed class ReportsRepository(AppDbContext context) : IReportsRepository
{
    public async Task<ReportPage<RequestReportRow>> GetRequestsAsync(
        ReportActor actor,
        ReportFilter filter)
    {
        var query = ApplyRequestFilters(
            VisibleRequests(actor),
            actor,
            filter,
            useCreatedDate: true);
        var totalCount = await query.CountAsync();
        var ordered = OrderRequests(query, filter);
        var items = await ordered
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(request => new RequestReportRow(
                request.Id,
                request.Title,
                request.Status.ToString(),
                request.RequestType == null ? null : request.RequestType.Code,
                request.RequestType == null
                    ? null
                    : request.RequestType.DisplayName,
                request.Priority == null
                    ? null
                    : request.Priority.Value.ToString(),
                request.CreatedAtUtc,
                request.CreatedByUserId,
                request.CreatedByUser == null
                    ? null
                    : request.CreatedByUser.FullName,
                request.DepartmentId,
                request.Department == null
                    ? null
                    : request.Department.Name,
                request.Amount))
            .ToListAsync();
        return new ReportPage<RequestReportRow>(
            items,
            totalCount,
            filter.Page,
            filter.PageSize);
    }

    public async Task<ReportPage<ApprovalHistoryReportRow>>
        GetApprovalHistoryAsync(
            ReportActor actor,
            ReportFilter filter)
    {
        var visibleRequests = ApplyRequestFilters(
                VisibleRequests(actor),
                actor,
                filter,
                useCreatedDate: false)
            .Select(request => request.Id);
        var workflowHistory = context.ApprovalDecisionHistory
            .AsNoTracking()
            .Where(history =>
                history.DecidedAtUtc >= filter.FromUtc &&
                history.DecidedAtUtc < filter.ToUtcExclusive &&
                visibleRequests.Contains(
                    history.WorkflowStage.WorkflowInstance.RequestId));
        if (actor.Role == UserRoles.Manager)
        {
            workflowHistory = workflowHistory.Where(history =>
                history.ManagerUserId == actor.UserId);
        }

        var projectedWorkflowHistory = workflowHistory
            .Select(history => new
            {
                RecordId = history.Id,
                Source = "Workflow",
                RequestId = history.WorkflowStage.WorkflowInstance.RequestId,
                RequestTitle = history.WorkflowStage.WorkflowInstance.Request.Title,
                RequestStatus = history.WorkflowStage.WorkflowInstance.Request.Status.ToString(),
                EmployeeUserId = history.WorkflowStage.WorkflowInstance.Request.CreatedByUserId,
                EmployeeName = history.WorkflowStage.WorkflowInstance.Request.CreatedByUser == null
                    ? null
                    : history.WorkflowStage.WorkflowInstance.Request.CreatedByUser.FullName,
                DepartmentName = history.WorkflowStage.WorkflowInstance.Request.Department == null
                    ? null
                    : history.WorkflowStage.WorkflowInstance.Request.Department.Name,
                ManagerUserId = history.ManagerUserId,
                ManagerName = history.ManagerUser.FullName,
                StageSequence = (int?)history.WorkflowStage.Sequence,
                Decision = history.Decision.ToString(),
                AssignedAtUtc = history.WorkflowStage.ActivatedAtUtc ?? history.DecidedAtUtc,
                DecidedAtUtc = history.DecidedAtUtc,
                Comment = history.Comment
            });
        var manualHistory = context.ApprovalAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.WorkflowStageId == null &&
                assignment.DecidedAtUtc.HasValue &&
                assignment.DecidedAtUtc >= filter.FromUtc &&
                assignment.DecidedAtUtc < filter.ToUtcExclusive &&
                assignment.Request.CreatedByUserId != null &&
                visibleRequests.Contains(assignment.RequestId));
        if (actor.Role == UserRoles.Manager)
        {
            manualHistory = manualHistory.Where(assignment =>
                assignment.ApproverUserId == actor.UserId);
        }

        var projectedManualHistory = manualHistory
            .Select(assignment => new
            {
                RecordId = assignment.Id,
                Source = "Manual",
                RequestId = assignment.RequestId,
                RequestTitle = assignment.Request.Title,
                RequestStatus = assignment.Request.Status.ToString(),
                EmployeeUserId = assignment.Request.CreatedByUserId,
                EmployeeName = assignment.Request.CreatedByUser == null
                    ? null
                    : assignment.Request.CreatedByUser.FullName,
                DepartmentName = assignment.Request.Department == null
                    ? null
                    : assignment.Request.Department.Name,
                ManagerUserId = assignment.ApproverUserId,
                ManagerName = assignment.ApproverUser.FullName,
                StageSequence = (int?)null,
                Decision = assignment.Status.ToString(),
                AssignedAtUtc = assignment.AssignedAtUtc,
                DecidedAtUtc = assignment.DecidedAtUtc!.Value,
                Comment = (string?)null
            });
        var combined = projectedWorkflowHistory.Concat(projectedManualHistory);
        var totalCount = await combined.CountAsync();
        // The anonymous projection keeps UNION ALL server-translatable; the
        // report DTO is materialized only after the database page is selected.
        var orderedRows = filter.SortBy switch
        {
            "requestTitle" => filter.SortDescending
                ? combined.OrderByDescending(row => row.RequestTitle)
                : combined.OrderBy(row => row.RequestTitle),
            "decision" => filter.SortDescending
                ? combined.OrderByDescending(row => row.Decision)
                : combined.OrderBy(row => row.Decision),
            "managerName" => filter.SortDescending
                ? combined.OrderByDescending(row => row.ManagerName)
                : combined.OrderBy(row => row.ManagerName),
            "employeeName" => filter.SortDescending
                ? combined.OrderByDescending(row => row.EmployeeName)
                : combined.OrderBy(row => row.EmployeeName),
            "departmentName" => filter.SortDescending
                ? combined.OrderByDescending(row => row.DepartmentName)
                : combined.OrderBy(row => row.DepartmentName),
            "stageSequence" => filter.SortDescending
                ? combined.OrderByDescending(row => row.StageSequence)
                : combined.OrderBy(row => row.StageSequence),
            _ => filter.SortDescending
                ? combined.OrderByDescending(row => row.DecidedAtUtc)
                : combined.OrderBy(row => row.DecidedAtUtc)
        };
        var ordered = orderedRows.ThenBy(row => row.Source)
            .ThenBy(row => row.RecordId);
        var items = await ordered
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();
        var reportItems = items.Select(row => new ApprovalHistoryReportRow(
            row.RecordId,
            row.Source,
            row.RequestId,
            row.RequestTitle,
            row.RequestStatus,
            row.EmployeeUserId,
            row.EmployeeName,
            row.DepartmentName,
            row.ManagerUserId,
            row.ManagerName,
            row.StageSequence,
            row.Decision,
            row.AssignedAtUtc,
            row.DecidedAtUtc,
            row.Comment)).ToArray();
        return new ReportPage<ApprovalHistoryReportRow>(
            reportItems,
            totalCount,
            filter.Page,
            filter.PageSize);
    }

    public async Task<ReportPage<WorkflowPerformanceReportRow>>
        GetWorkflowPerformanceAsync(
            ReportActor actor,
            ReportFilter filter)
    {
        var visibleRequests = ApplyRequestFilters(
                VisibleRequests(actor),
                actor,
                filter,
                useCreatedDate: false)
            .Select(request => request.Id);
        var query = context.ApprovalWorkflowInstances
            .AsNoTracking()
            .Where(instance =>
                instance.StartedAtUtc >= filter.FromUtc &&
                instance.StartedAtUtc < filter.ToUtcExclusive &&
                visibleRequests.Contains(instance.RequestId));

        if (actor.Role == UserRoles.Manager)
        {
            query = query.Where(instance =>
                instance.Request.Assignments.Any(assignment =>
                    assignment.ApproverUserId == actor.UserId));
        }
        else if (actor.Role == UserRoles.Employee)
        {
            query = query.Where(instance =>
                instance.Request.CreatedByUserId == actor.UserId);
        }

        if (filter.ManagerUserId is int managerId)
        {
            query = query.Where(instance =>
                instance.Stages.Any(stage =>
                    stage.ApproverUserId == managerId) ||
                instance.Request.Assignments.Any(assignment =>
                    assignment.ApproverUserId == managerId));
        }

        if (filter.Search.Length > 0)
        {
            var search = filter.Search;
            query = query.Where(instance =>
                instance.Request.Title.Contains(search) ||
                instance.WorkflowRule.Name.Contains(search) ||
                (instance.Request.CreatedByUser != null &&
                 instance.Request.CreatedByUser.FullName.Contains(search)));
        }

        if (filter.DepartmentId is int departmentId)
        {
            query = query.Where(instance =>
                instance.Request.DepartmentId == departmentId);
        }

        if (filter.RequestTypeId is int requestTypeId)
        {
            query = query.Where(instance =>
                instance.Request.RequestTypeId == requestTypeId);
        }

        if (filter.Status is RequestStatus status)
        {
            query = query.Where(instance =>
                instance.Request.Status == status);
        }

        if (filter.Priority is RequestPriority priority)
        {
            query = query.Where(instance =>
                instance.Request.Priority == priority);
        }

        if (filter.EmployeeUserId is int employeeUserId)
        {
            query = query.Where(instance =>
                instance.Request.CreatedByUserId == employeeUserId);
        }

        var totalCount = await query.CountAsync();
        var ordered = OrderWorkflowPerformance(query, filter);
        var items = await ordered
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(instance => new WorkflowPerformanceReportRow(
                instance.RequestId,
                instance.Request.Title,
                instance.WorkflowRule.Name,
                instance.RuleVersion,
                instance.Status.ToString(),
                instance.Request.Status.ToString(),
                instance.Stages.Count,
                instance.Stages.Count(stage =>
                    stage.Status == ApprovalWorkflowStageStatus.Approved),
                instance.Stages.Count(stage =>
                    stage.Status == ApprovalWorkflowStageStatus.Rejected),
                instance.Stages.Count(stage =>
                    stage.Status == ApprovalWorkflowStageStatus.Active),
                instance.StartedAtUtc,
                instance.CompletedAtUtc,
                null,
                instance.Request.CreatedByUserId,
                instance.Request.CreatedByUser == null
                    ? null
                    : instance.Request.CreatedByUser.FullName,
                instance.Request.Department == null
                    ? null
                    : instance.Request.Department.Name))
            .ToListAsync();
        return new ReportPage<WorkflowPerformanceReportRow>(
            items,
            totalCount,
            filter.Page,
            filter.PageSize);
    }

    public async Task<ReportPage<LeaveReportRow>> GetLeaveReportAsync(
        ReportActor actor,
        ReportFilter filter)
    {
        var query = context.ApprovalRequests
            .AsNoTracking()
            .Where(request =>
                request.LeaveDetails != null &&
                request.LeaveDetails.CancellationOfRequestId == null &&
                request.CreatedAtUtc >= filter.FromUtc &&
                request.CreatedAtUtc < filter.ToUtcExclusive &&
                request.CreatedByUserId != null);

        if (actor.Role == UserRoles.Employee)
        {
            query = query.Where(request =>
                request.CreatedByUserId == actor.UserId);
        }
        else if (actor.Role == UserRoles.Manager)
        {
            query = query.Where(request =>
                request.CreatedByUser != null &&
                request.CreatedByUser.Role == UserRoles.Employee &&
                request.CreatedByUser.IsActive &&
                request.CreatedByUser.DepartmentId == actor.DepartmentId);
        }

        if (filter.ManagerUserId is int managerId)
        {
            query = query.Where(request =>
                request.Assignments.Any(assignment =>
                    assignment.ApproverUserId == managerId));
        }

        if (filter.Search.Length > 0)
        {
            var search = filter.Search;
            query = query.Where(request =>
                request.Title.Contains(search) ||
                request.LeaveDetails!.LeaveType.Contains(search) ||
                (request.CreatedByUser != null &&
                 request.CreatedByUser.FullName.Contains(search)));
        }

        if (filter.DepartmentId is int departmentId)
        {
            query = query.Where(request =>
                request.DepartmentId == departmentId);
        }

        if (filter.RequestTypeId is int requestTypeId)
        {
            query = query.Where(request =>
                request.RequestTypeId == requestTypeId);
        }

        if (filter.Status is RequestStatus status)
        {
            query = query.Where(request => request.Status == status);
        }

        if (filter.Priority is RequestPriority priority)
        {
            query = query.Where(request => request.Priority == priority);
        }

        if (filter.EmployeeUserId is int employeeUserId)
        {
            query = query.Where(request =>
                request.CreatedByUserId == employeeUserId);
        }

        query = query.Where(request =>
            request.LeaveDetails!.StartDate <= filter.To &&
            request.LeaveDetails.EndDate >= filter.From);

        var totalCount = await query.CountAsync();
        var ordered = OrderLeaveReports(query, filter);
        var items = await ordered
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(request => new LeaveReportRow(
                request.Id,
                request.Status.ToString(),
                request.Priority == null
                    ? null
                    : request.Priority.Value.ToString(),
                request.CreatedAtUtc,
                request.CreatedByUserId!.Value,
                request.CreatedByUser!.FullName,
                request.DepartmentId,
                request.Department == null
                    ? null
                    : request.Department.Name,
                request.LeaveDetails!.ConfiguredLeaveTypeId,
                request.LeaveDetails.LeaveType,
                request.LeaveDetails.StartDate,
                request.LeaveDetails.EndDate,
                request.LeaveDetails.TotalLeaveDays,
                request.LeaveDetails.CalculatedLeaveUnits,
                null,
                null,
                null,
                null,
                null))
            .ToListAsync();
        return new ReportPage<LeaveReportRow>(
            items,
            totalCount,
            filter.Page,
            filter.PageSize);
    }

    public async Task<ReportFilterOptions> GetFilterOptionsAsync(
        ReportActor actor)
    {
        var departments = context.Departments.AsNoTracking();
        var employees = context.Users.AsNoTracking()
            .Where(user => user.IsActive && user.Role == UserRoles.Employee);
        var managers = context.Users.AsNoTracking()
            .Where(user => user.IsActive && user.Role == UserRoles.Manager);

        if (actor.Role != UserRoles.Admin)
        {
            if (actor.DepartmentId is int departmentId)
            {
                departments = departments.Where(department =>
                    department.Id == departmentId);
                employees = employees.Where(user =>
                    user.DepartmentId == departmentId);
            }
            else
            {
                departments = departments.Where(_ => false);
                employees = employees.Where(_ => false);
            }

            if (actor.Role == UserRoles.Manager)
            {
                managers = managers.Where(user => user.Id == actor.UserId);
            }
            else
            {
                employees = employees.Where(user => user.Id == actor.UserId);
                managers = managers.Where(_ => false);
            }
        }

        return new ReportFilterOptions(
            await departments.OrderBy(department => department.Name)
                .Select(department => new ReportOption(
                    department.Id,
                    department.Name))
                .ToListAsync(),
            await employees.OrderBy(user => user.FullName)
                .Select(user => new ReportOption(user.Id, user.FullName))
                .ToListAsync(),
            await managers.OrderBy(user => user.FullName)
                .Select(user => new ReportOption(user.Id, user.FullName))
                .ToListAsync(),
            await context.RequestTypes.AsNoTracking()
                .Where(type => type.IsEnabled)
                .OrderBy(type => type.DisplayName)
                .Select(type => new ReportOption(type.Id, type.DisplayName))
                .ToListAsync());
    }

    private IQueryable<ApprovalRequest> VisibleRequests(ReportActor actor)
    {
        var requests = context.ApprovalRequests.AsNoTracking();
        if (actor.Role == UserRoles.Employee)
        {
            return requests.Where(request =>
                request.CreatedByUserId == actor.UserId);
        }

        if (actor.Role == UserRoles.Manager)
        {
            return requests.Where(request =>
                request.CreatedByUserId != null &&
                request.Assignments.Any(assignment =>
                    assignment.ApproverUserId == actor.UserId));
        }

        return requests;
    }

    private static IQueryable<ApprovalRequest> ApplyRequestFilters(
        IQueryable<ApprovalRequest> query,
        ReportActor actor,
        ReportFilter filter,
        bool useCreatedDate)
    {
        if (useCreatedDate)
        {
            query = query.Where(request =>
                request.CreatedAtUtc >= filter.FromUtc &&
                request.CreatedAtUtc < filter.ToUtcExclusive);
        }

        if (actor.Role == UserRoles.Manager)
        {
            query = query.Where(request =>
                request.Assignments.Any(assignment =>
                    assignment.ApproverUserId == actor.UserId));
        }

        if (filter.ManagerUserId is int managerId)
        {
            query = query.Where(request =>
                request.Assignments.Any(assignment =>
                    assignment.ApproverUserId == managerId));
        }

        if (filter.Search.Length > 0)
        {
            var search = filter.Search;
            query = query.Where(request =>
                request.Title.Contains(search) ||
                (request.Description != null &&
                 request.Description.Contains(search)) ||
                (request.CreatedByUser != null &&
                 request.CreatedByUser.FullName.Contains(search)) ||
                (request.RequestType != null &&
                 request.RequestType.DisplayName.Contains(search)));
        }

        if (filter.DepartmentId is int departmentId)
        {
            query = query.Where(request =>
                request.DepartmentId == departmentId);
        }

        if (filter.RequestTypeId is int requestTypeId)
        {
            query = query.Where(request =>
                request.RequestTypeId == requestTypeId);
        }

        if (filter.Status is RequestStatus status)
        {
            query = query.Where(request => request.Status == status);
        }

        if (filter.Priority is RequestPriority priority)
        {
            query = query.Where(request => request.Priority == priority);
        }

        if (filter.EmployeeUserId is int employeeUserId)
        {
            query = query.Where(request =>
                request.CreatedByUserId == employeeUserId);
        }

        return query;
    }

    private static IOrderedQueryable<ApprovalRequest> OrderRequests(
        IQueryable<ApprovalRequest> query,
        ReportFilter filter)
    {
        IOrderedQueryable<ApprovalRequest> ordered = filter.SortBy switch
        {
            "title" => filter.SortDescending
                ? query.OrderByDescending(request => request.Title)
                : query.OrderBy(request => request.Title),
            "status" => filter.SortDescending
                ? query.OrderByDescending(request => request.Status)
                : query.OrderBy(request => request.Status),
            "requestTypeName" => filter.SortDescending
                ? query.OrderByDescending(request =>
                    request.RequestType!.DisplayName)
                : query.OrderBy(request => request.RequestType!.DisplayName),
            "priority" => filter.SortDescending
                ? query.OrderByDescending(request => request.Priority)
                : query.OrderBy(request => request.Priority),
            "employeeName" => filter.SortDescending
                ? query.OrderByDescending(request =>
                    request.CreatedByUser!.FullName)
                : query.OrderBy(request => request.CreatedByUser!.FullName),
            "departmentName" => filter.SortDescending
                ? query.OrderByDescending(request =>
                    request.Department!.Name)
                : query.OrderBy(request => request.Department!.Name),
            _ => filter.SortDescending
                ? query.OrderByDescending(request => request.CreatedAtUtc)
                : query.OrderBy(request => request.CreatedAtUtc)
        };
        return ordered.ThenBy(request => request.Id);
    }

    private static IOrderedQueryable<ApprovalWorkflowInstance>
        OrderWorkflowPerformance(
            IQueryable<ApprovalWorkflowInstance> query,
            ReportFilter filter)
    {
        IOrderedQueryable<ApprovalWorkflowInstance> ordered = filter.SortBy switch
        {
            "requestTitle" => filter.SortDescending
                ? query.OrderByDescending(instance => instance.Request.Title)
                : query.OrderBy(instance => instance.Request.Title),
            "workflowName" => filter.SortDescending
                ? query.OrderByDescending(instance => instance.WorkflowRule.Name)
                : query.OrderBy(instance => instance.WorkflowRule.Name),
            "workflowStatus" => filter.SortDescending
                ? query.OrderByDescending(instance => instance.Status)
                : query.OrderBy(instance => instance.Status),
            "requestStatus" => filter.SortDescending
                ? query.OrderByDescending(instance => instance.Request.Status)
                : query.OrderBy(instance => instance.Request.Status),
            "employeeName" => filter.SortDescending
                ? query.OrderByDescending(instance =>
                    instance.Request.CreatedByUser!.FullName)
                : query.OrderBy(instance =>
                    instance.Request.CreatedByUser!.FullName),
            "departmentName" => filter.SortDescending
                ? query.OrderByDescending(instance =>
                    instance.Request.Department!.Name)
                : query.OrderBy(instance =>
                    instance.Request.Department!.Name),
            _ => filter.SortDescending
                ? query.OrderByDescending(instance => instance.StartedAtUtc)
                : query.OrderBy(instance => instance.StartedAtUtc)
        };
        return ordered.ThenBy(instance => instance.Id);
    }

    private static IOrderedQueryable<ApprovalRequest> OrderLeaveReports(
        IQueryable<ApprovalRequest> query,
        ReportFilter filter)
    {
        IOrderedQueryable<ApprovalRequest> ordered = filter.SortBy switch
        {
            "startDate" => filter.SortDescending
                ? query.OrderByDescending(request =>
                    request.LeaveDetails!.StartDate)
                : query.OrderBy(request => request.LeaveDetails!.StartDate),
            "endDate" => filter.SortDescending
                ? query.OrderByDescending(request =>
                    request.LeaveDetails!.EndDate)
                : query.OrderBy(request => request.LeaveDetails!.EndDate),
            "leaveType" => filter.SortDescending
                ? query.OrderByDescending(request =>
                    request.LeaveDetails!.LeaveType)
                : query.OrderBy(request => request.LeaveDetails!.LeaveType),
            "requestStatus" => filter.SortDescending
                ? query.OrderByDescending(request => request.Status)
                : query.OrderBy(request => request.Status),
            "employeeName" => filter.SortDescending
                ? query.OrderByDescending(request =>
                    request.CreatedByUser!.FullName)
                : query.OrderBy(request => request.CreatedByUser!.FullName),
            "departmentName" => filter.SortDescending
                ? query.OrderByDescending(request =>
                    request.Department!.Name)
                : query.OrderBy(request => request.Department!.Name),
            "calculatedLeaveUnits" => filter.SortDescending
                ? query.OrderByDescending(request =>
                    request.LeaveDetails!.CalculatedLeaveUnits)
                : query.OrderBy(request =>
                    request.LeaveDetails!.CalculatedLeaveUnits),
            "createdAtUtc" => filter.SortDescending
                ? query.OrderByDescending(request => request.CreatedAtUtc)
                : query.OrderBy(request => request.CreatedAtUtc),
            _ => filter.SortDescending
                ? query.OrderByDescending(request =>
                    request.LeaveDetails!.StartDate)
                : query.OrderBy(request => request.LeaveDetails!.StartDate)
        };
        return ordered.ThenBy(request => request.Id);
    }
}
