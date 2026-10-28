using FlowDesk.Application.DTOs;
using FlowDesk.Application.Errors;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.Services;

public sealed class ReportsService(
    IReportsRepository repository,
    IUserRepository userRepository,
    LeaveAccountingService accountingService)
{
    private static readonly HashSet<string> RequestSortFields =
        ["createdAtUtc", "title", "status", "requestTypeName", "priority",
            "employeeName", "departmentName"];
    private static readonly HashSet<string> ApprovalSortFields =
        ["decidedAtUtc", "requestTitle", "decision", "managerName",
            "employeeName", "departmentName", "stageSequence"];
    private static readonly HashSet<string> WorkflowSortFields =
        ["startedAtUtc", "requestTitle", "workflowName", "workflowStatus",
            "requestStatus", "elapsedHours", "employeeName", "departmentName"];
    private static readonly HashSet<string> LeaveSortFields =
        ["createdAtUtc", "startDate", "endDate", "leaveType", "requestStatus",
            "employeeName", "departmentName", "calculatedLeaveUnits"];

    public async Task<ReportPage<RequestReportRow>> GetRequestsAsync(
        int actorUserId,
        string tokenRole,
        ReportQueryInput input)
    {
        var (actor, filter) = await GetActorAndFilterAsync(
            actorUserId,
            tokenRole,
            input,
            RequestSortFields);
        return await repository.GetRequestsAsync(actor, filter);
    }

    public async Task<ReportPage<ApprovalHistoryReportRow>>
        GetApprovalHistoryAsync(
            int actorUserId,
            string tokenRole,
            ReportQueryInput input)
    {
        var (actor, filter) = await GetActorAndFilterAsync(
            actorUserId,
            tokenRole,
            input,
            ApprovalSortFields);
        return await repository.GetApprovalHistoryAsync(actor, filter);
    }

    public async Task<ReportPage<WorkflowPerformanceReportRow>>
        GetWorkflowPerformanceAsync(
            int actorUserId,
            string tokenRole,
            ReportQueryInput input)
    {
        var (actor, filter) = await GetActorAndFilterAsync(
            actorUserId,
            tokenRole,
            input,
            WorkflowSortFields);
        var page = await repository.GetWorkflowPerformanceAsync(actor, filter);
        var items = page.Items.Select(item => item with
        {
            ElapsedHours = item.CompletedAtUtc is DateTime completedAtUtc &&
                completedAtUtc >= item.StartedAtUtc
                    ? (completedAtUtc - item.StartedAtUtc).TotalHours
                    : null
        }).ToArray();
        return page with { Items = items };
    }

    public async Task<ReportPage<LeaveReportRow>> GetLeaveReportAsync(
        int actorUserId,
        string tokenRole,
        ReportQueryInput input)
    {
        var (actor, filter) = await GetActorAndFilterAsync(
            actorUserId,
            tokenRole,
            input,
            LeaveSortFields);
        var page = await repository.GetLeaveReportAsync(actor, filter);
        var employeeIds = page.Items
            .Select(item => item.EmployeeUserId)
            .Distinct()
            .ToArray();
        var balances = await accountingService.GetBalancesForReportAsync(
            actor.UserId,
            employeeIds,
            filter.To);
        var items = page.Items.Select(item =>
        {
            var employeeBalances = balances.GetValueOrDefault(
                item.EmployeeUserId) ?? [];
            var balance = item.LeaveTypeId is int leaveTypeId
                ? employeeBalances.FirstOrDefault(candidate =>
                    candidate.LeaveTypeId == leaveTypeId)
                : FindUniqueBalanceByName(employeeBalances, item.LeaveType);
            return item with
            {
                Earned = balance?.Earned,
                Used = balance?.Used,
                Pending = balance?.Pending,
                Scheduled = balance?.Scheduled,
                Available = balance?.Available
            };
        }).ToArray();
        return page with { Items = items };
    }

    private static LeaveBalanceSummary? FindUniqueBalanceByName(
        IReadOnlyList<LeaveBalanceSummary> balances,
        string leaveTypeName)
    {
        var matches = balances.Where(candidate =>
                string.Equals(
                    candidate.LeaveTypeName,
                    leaveTypeName,
                    StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public async Task<ReportFilterOptions> GetFilterOptionsAsync(
        int actorUserId,
        string tokenRole)
    {
        var actor = await GetActorAsync(actorUserId, tokenRole);
        return await repository.GetFilterOptionsAsync(actor);
    }

    private async Task<(ReportActor Actor, ReportFilter Filter)>
        GetActorAndFilterAsync(
            int actorUserId,
            string tokenRole,
            ReportQueryInput input,
            IReadOnlySet<string> allowedSortFields)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actor = await GetActorAsync(actorUserId, tokenRole);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = input.From ?? today.AddDays(-29);
        var to = input.To ?? today;
        if (to < from || to.DayNumber - from.DayNumber > 365)
        {
            throw new LeaveInvalidInputException(
                "The date range must be valid and no longer than 366 days.");
        }

        if (input.Page is < 1 or > 100_000 ||
            input.PageSize is < 1 or > 100 ||
            input.DepartmentId is <= 0 ||
            input.RequestTypeId is <= 0 ||
            input.EmployeeUserId is <= 0 ||
            input.ManagerUserId is <= 0 ||
            input.Search?.Trim().Length > 100)
        {
            throw new LeaveInvalidInputException(
                "The report filters or pagination values are invalid.");
        }

        if (actor.Role == UserRoles.Employee &&
            (input.EmployeeUserId is int employeeId &&
                 employeeId != actor.UserId ||
             input.ManagerUserId.HasValue))
        {
            throw new LeaveUnauthorizedException();
        }

        if (actor.Role == UserRoles.Manager &&
            input.ManagerUserId is int managerId &&
            managerId != actor.UserId)
        {
            throw new LeaveUnauthorizedException();
        }

        var sortBy = string.IsNullOrWhiteSpace(input.SortBy)
            ? allowedSortFields.Contains("createdAtUtc")
                ? "createdAtUtc"
                : allowedSortFields.First()
            : input.SortBy.Trim();
        if (!allowedSortFields.Contains(sortBy))
        {
            throw new LeaveInvalidInputException(
                "The selected report sort field is not supported.");
        }

        var sortDirection = string.IsNullOrWhiteSpace(input.SortDirection)
            ? "desc"
            : input.SortDirection.Trim();
        if (!string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase))
        {
            throw new LeaveInvalidInputException(
                "Sort direction must be asc or desc.");
        }

        var status = ParseEnum<RequestStatus>(input.Status, "status");
        var priority = ParseEnum<RequestPriority>(input.Priority, "priority");
        var fromUtc = DateTime.SpecifyKind(
            from.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Utc);
        var toUtcExclusive = DateTime.SpecifyKind(
            to.AddDays(1).ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Utc);

        return (actor, new ReportFilter(
            from,
            to,
            fromUtc,
            toUtcExclusive,
            input.Page,
            input.PageSize,
            sortBy,
            string.Equals(
                sortDirection,
                "desc",
                StringComparison.OrdinalIgnoreCase),
            input.Search?.Trim() ?? string.Empty,
            input.DepartmentId,
            input.RequestTypeId,
            status,
            priority,
            actor.Role == UserRoles.Employee
                ? actor.UserId
                : input.EmployeeUserId,
            actor.Role == UserRoles.Manager
                ? actor.UserId
                : input.ManagerUserId));
    }

    private async Task<ReportActor> GetActorAsync(
        int actorUserId,
        string tokenRole)
    {
        var actor = actorUserId > 0
            ? await userRepository.GetByIdAsync(actorUserId)
            : null;
        if (actor is not { IsActive: true } ||
            !string.Equals(actor.Role, tokenRole, StringComparison.Ordinal) ||
            actor.Role is not UserRoles.Admin and
                not UserRoles.Manager and not UserRoles.Employee)
        {
            throw new LeaveUnauthorizedException();
        }

        return new ReportActor(actor.Id, actor.Role, actor.DepartmentId);
    }

    private static TEnum? ParseEnum<TEnum>(
        string? value,
        string fieldName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Enum.TryParse<TEnum>(value, true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw new LeaveInvalidInputException(
                $"The {fieldName} report filter is invalid.");
        }

        return parsed;
    }
}
