using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.Services;

public sealed class DashboardAnalyticsService
{
    private static readonly (string Code, string Name)[] KnownRequestTypes =
    [
        ("MATERIAL_PURCHASE", "Material Purchase"),
        ("LEAVE", "Leave Request"),
        ("EXPENSE_REIMBURSEMENT", "Expense Reimbursement"),
        ("IT_ACCESS", "IT Access Request"),
        ("TRAVEL", "Travel Request"),
        ("WORK_FROM_HOME", "Work From Home")
    ];

    private readonly IDashboardAnalyticsRepository _repository;

    public DashboardAnalyticsService(
        IDashboardAnalyticsRepository repository)
    {
        _repository = repository;
    }

    public async Task<DashboardAnalyticsDto?> GetAsync(
        int userId,
        string tokenRole,
        DateOnly from,
        DateOnly to)
    {
        if (userId <= 0 || to < from || to.DayNumber - from.DayNumber > 365)
        {
            throw new ArgumentOutOfRangeException(
                nameof(to),
                "The date range must be valid and no longer than 366 days.");
        }

        var access = await _repository.GetUserAccessAsync(userId);
        if (access is null ||
            !access.IsActive ||
            !string.Equals(access.Role, tokenRole, StringComparison.Ordinal) ||
            access.Role is not UserRoles.Admin and
                not UserRoles.Manager and not UserRoles.Employee)
        {
            return null;
        }

        var fromUtc = DateTime.SpecifyKind(
            from.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Utc);
        var toUtcExclusive = DateTime.SpecifyKind(
            to.AddDays(1).ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Utc);

        var requests = access.Role switch
        {
            UserRoles.Admin => await _repository.GetRequestsAsync(
                ownerUserId: null,
                includeLegacy: true,
                fromUtc,
                toUtcExclusive),
            UserRoles.Employee => await _repository.GetRequestsAsync(
                userId,
                includeLegacy: false,
                fromUtc,
                toUtcExclusive),
            _ => await _repository.GetManagerRequestsAsync(
                userId,
                fromUtc,
                toUtcExclusive)
        };

        var decisions = access.Role == UserRoles.Employee
            ? []
            : await _repository.GetDecisionsAsync(
                access.Role == UserRoles.Manager ? userId : null,
                fromUtc,
                toUtcExclusive);

        var assignments = access.Role == UserRoles.Manager
            ? await _repository.GetManagerAssignmentsAsync(
                userId,
                fromUtc,
                toUtcExclusive)
            : [];

        var activeUsers = access.Role == UserRoles.Admin
            ? await _repository.GetActiveUserCountsAsync()
            : new DashboardActiveUserCountsDto(0, 0);
        var asOfUtc = DateTime.UtcNow;
        var pendingStages = access.Role is UserRoles.Admin or UserRoles.Manager
            ? await _repository.GetPendingWorkflowStagesAsync(
                access.Role == UserRoles.Manager ? userId : null,
                fromUtc,
                toUtcExclusive,
                asOfUtc)
            : [];

        return Build(
            access.Role,
            from,
            to,
            requests,
            assignments,
            decisions,
            pendingStages,
            activeUsers,
            asOfUtc);
    }

    private static DashboardAnalyticsDto Build(
        string role,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<DashboardRequestRecord> requests,
        IReadOnlyList<DashboardAssignmentRecord> assignments,
        IReadOnlyList<DashboardDecisionRecord> decisions,
        IReadOnlyList<DashboardPendingStageRecord> pendingStages,
        DashboardActiveUserCountsDto activeUsers,
        DateTime asOfUtc)
    {
        var statusDistribution = Enum.GetValues<RequestStatus>()
            .Select(status => new AnalyticsCountDto(
                status.ToString(),
                requests.Count(x => x.Status == status)))
            .ToArray();

        var submissionTrend = BuildTrend(
            requests.Select(x => x.CreatedAtUtc),
            from,
            to);

        var departmentPerformance = role == UserRoles.Admin
            ? requests
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Department)
                    ? "Unassigned"
                    : x.Department)
                .Select(group => new DepartmentPerformanceDto(
                    group.Key!,
                    group.Count(x => x.Status == RequestStatus.Approved),
                    group.Count(x => x.Status == RequestStatus.Pending),
                    group.Count(x => x.Status == RequestStatus.Rejected)))
                .OrderBy(x => x.Department)
                .ToArray()
            : [];

        var approvalPerformance = role == UserRoles.Admin
            ? BuildDecisionPerformance(decisions)
            : [];

        var requestTypes = role is UserRoles.Admin or UserRoles.Employee
            ? BuildRequestTypes(requests)
            : [];

        var uniqueAssignments = assignments
            .GroupBy(x => x.RequestId)
            .Select(group => group
                .OrderByDescending(x => x.AssignedAtUtc)
                .First())
            .ToArray();

        var assignedRequestTrend = role == UserRoles.Manager
            ? BuildTrend(
                uniqueAssignments.Select(x => x.AssignedAtUtc),
                from,
                to)
            : [];

        var managerRequestStatus = role == UserRoles.Manager
            ? new[]
            {
                new AnalyticsCountDto(
                    "Pending",
                    uniqueAssignments.Count(x =>
                        x.AssignmentStatus ==
                        ApprovalAssignmentStatus.Pending)),
                new AnalyticsCountDto(
                    "Completed",
                    uniqueAssignments.Count(x =>
                        x.AssignmentStatus !=
                        ApprovalAssignmentStatus.Pending))
            }
            : [];

        var decisionActivity = role is UserRoles.Admin or UserRoles.Manager
            ? BuildTrend(decisions.Select(x => x.DecidedAtUtc), from, to)
            : [];

        var validDurations = decisions
            .Where(x =>
                x.ActivatedAtUtc is DateTime activated &&
                activated <= x.DecidedAtUtc)
            .Select(x =>
                (x.DecidedAtUtc - x.ActivatedAtUtc!.Value).TotalHours)
            .ToArray();
        double? averageDecisionTime = validDurations.Length == 0
            ? null
            : validDurations.Average();

        var priorityDistribution = role is UserRoles.Admin or UserRoles.Manager
            ? Enum.GetValues<RequestPriority>()
                .Select(priority => new AnalyticsCountDto(
                    priority.ToString(),
                    role == UserRoles.Manager
                        ? uniqueAssignments.Count(x => x.Priority == priority)
                        : requests.Count(x => x.Priority == priority)))
                .Append(new AnalyticsCountDto(
                    "Unspecified",
                    role == UserRoles.Manager
                        ? uniqueAssignments.Count(x => x.Priority is null)
                        : requests.Count(x => x.Priority is null)))
                .ToArray()
            : [];

        var approvalProgress = role == UserRoles.Employee
            ? statusDistribution
                .Append(new AnalyticsCountDto(
                    "Awaiting routing",
                    requests.Count(x =>
                        x.Status == RequestStatus.Pending &&
                        !x.HasWorkflow)))
                .ToArray()
            : [];

        var overdueStageRecords = pendingStages
            .Where(stage => stage.DueAtUtc is DateTime due &&
                due < asOfUtc)
            .ToArray();
        var workflowBottlenecks = role == UserRoles.Admin
            ? pendingStages
                .GroupBy(stage => stage.Sequence)
                .Select(group => new WorkflowBottleneckDto(
                    $"Stage {group.Key}",
                    group.Count(),
                    group.Count(stage => stage.DueAtUtc is DateTime due &&
                        due < asOfUtc),
                    group.Where(stage =>
                            stage.ActivatedAtUtc is DateTime activated &&
                            activated <= asOfUtc)
                        .Select(stage =>
                            (asOfUtc -
                             stage.ActivatedAtUtc!.Value).TotalHours)
                        .DefaultIfEmpty()
                        .Average()))
                .OrderBy(item => int.Parse(
                    item.Stage.AsSpan("Stage ".Length),
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToArray()
            : [];

        var adminSummary = role == UserRoles.Admin
            ? CreateAdminSummary(
                requests,
                decisions,
                overdueStageRecords.Length,
                activeUsers)
            : null;
        var managerSummary = role == UserRoles.Manager
            ? CreateManagerSummary(
                uniqueAssignments,
                decisions,
                overdueStageRecords.Length)
            : null;
        var employeeSummary = role == UserRoles.Employee
            ? CreateEmployeeSummary(requests)
            : null;

        return new DashboardAnalyticsDto(
            role,
            from,
            to,
            requests.Count,
            statusDistribution,
            submissionTrend,
            departmentPerformance,
            approvalPerformance,
            requestTypes,
            assignedRequestTrend,
            managerRequestStatus,
            decisionActivity,
            averageDecisionTime,
            priorityDistribution,
            approvalProgress,
            adminSummary,
            managerSummary,
            employeeSummary,
            workflowBottlenecks);
    }

    private static AdminAnalyticsSummaryDto CreateAdminSummary(
        IReadOnlyList<DashboardRequestRecord> requests,
        IReadOnlyList<DashboardDecisionRecord> decisions,
        int overdueRequests,
        DashboardActiveUserCountsDto activeUsers)
    {
        var approved = requests.Count(request =>
            request.Status == RequestStatus.Approved);
        var rejected = requests.Count(request =>
            request.Status == RequestStatus.Rejected);
        var validDurations = decisions
            .Where(decision =>
                decision.ActivatedAtUtc is DateTime activated &&
                activated <= decision.DecidedAtUtc)
            .Select(decision =>
                (decision.DecidedAtUtc -
                 decision.ActivatedAtUtc!.Value).TotalHours)
            .ToArray();

        return new AdminAnalyticsSummaryDto(
            requests.Count,
            requests.Count(request =>
                request.Status == RequestStatus.Pending),
            approved,
            rejected,
            requests.Count(request =>
                request.Status == RequestStatus.Cancelled),
            approved + rejected == 0
                ? null
                : approved * 100d / (approved + rejected),
            validDurations.Length == 0
                ? null
                : validDurations.Average(),
            overdueRequests,
            activeUsers.Employees,
            activeUsers.Managers);
    }

    private static ManagerAnalyticsSummaryDto CreateManagerSummary(
        IReadOnlyList<DashboardAssignmentRecord> assignments,
        IReadOnlyList<DashboardDecisionRecord> decisions,
        int overdueApprovals)
    {
        var validDurations = decisions
            .Where(decision =>
                decision.ActivatedAtUtc is DateTime activated &&
                activated <= decision.DecidedAtUtc)
            .Select(decision =>
                (decision.DecidedAtUtc -
                 decision.ActivatedAtUtc!.Value).TotalHours)
            .ToArray();

        return new ManagerAnalyticsSummaryDto(
            assignments.Count,
            assignments.Count(assignment =>
                assignment.AssignmentStatus ==
                ApprovalAssignmentStatus.Pending),
            assignments.Count(assignment =>
                assignment.AssignmentStatus ==
                ApprovalAssignmentStatus.Approved),
            assignments.Count(assignment =>
                assignment.AssignmentStatus ==
                ApprovalAssignmentStatus.Rejected),
            overdueApprovals,
            validDurations.Length == 0
                ? null
                : validDurations.Average());
    }

    private static EmployeeAnalyticsSummaryDto CreateEmployeeSummary(
        IReadOnlyList<DashboardRequestRecord> requests)
    {
        return new EmployeeAnalyticsSummaryDto(
            requests.Count,
            requests.Count(request =>
                request.Status == RequestStatus.Pending),
            requests.Count(request =>
                request.Status == RequestStatus.Approved),
            requests.Count(request =>
                request.Status == RequestStatus.Rejected),
            requests.Count(request =>
                request.Status == RequestStatus.Cancelled));
    }

    private static IReadOnlyList<AnalyticsPointDto> BuildTrend(
        IEnumerable<DateTime> timestamps,
        DateOnly from,
        DateOnly to)
    {
        var useMonthlyBuckets = to.DayNumber - from.DayNumber > 31;
        var grouped = timestamps
            .Select(timestamp => DateOnly.FromDateTime(timestamp))
            .GroupBy(date => useMonthlyBuckets
                ? new DateOnly(date.Year, date.Month, 1)
                : date)
            .ToDictionary(group => group.Key, group => group.Count());

        var points = new List<AnalyticsPointDto>();
        var cursor = useMonthlyBuckets
            ? new DateOnly(from.Year, from.Month, 1)
            : from;
        var lastBucket = useMonthlyBuckets
            ? new DateOnly(to.Year, to.Month, 1)
            : to;

        while (cursor <= lastBucket)
        {
            points.Add(new AnalyticsPointDto(
                cursor.ToString(
                    useMonthlyBuckets ? "yyyy-MM" : "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture),
                grouped.GetValueOrDefault(cursor)));
            cursor = useMonthlyBuckets
                ? cursor.AddMonths(1)
                : cursor.AddDays(1);
        }

        return points;
    }

    private static IReadOnlyList<DecisionTimeDto> BuildDecisionPerformance(
        IReadOnlyList<DashboardDecisionRecord> decisions)
    {
        return decisions
            .Where(x =>
                x.ActivatedAtUtc is DateTime activated &&
                activated <= x.DecidedAtUtc)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Department)
                ? "Unassigned"
                : x.Department)
            .Select(group => new DecisionTimeDto(
                group.Key!,
                group.Average(x =>
                    (x.DecidedAtUtc - x.ActivatedAtUtc!.Value).TotalHours),
                group.Count()))
            .OrderBy(x => x.Department)
            .ToArray();
    }

    private static IReadOnlyList<RequestTypeAnalyticsDto> BuildRequestTypes(
        IReadOnlyList<DashboardRequestRecord> requests)
    {
        var known = KnownRequestTypes
            .Select(type => new RequestTypeAnalyticsDto(
                type.Code,
                type.Name,
                requests.Count(x => x.RequestTypeCode == type.Code)))
            .ToList();

        var unknown = requests
            .Where(x => x.RequestTypeCode is not null &&
                KnownRequestTypes.All(type =>
                    type.Code != x.RequestTypeCode))
            .GroupBy(x => x.RequestTypeCode!)
            .Select(group => new RequestTypeAnalyticsDto(
                group.Key,
                group.First().RequestTypeName ?? "Other / Legacy",
                group.Count()));

        known.Add(new RequestTypeAnalyticsDto(
            "OTHER_LEGACY",
            "Other / Legacy",
            requests.Count(x => x.RequestTypeCode is null)));
        known.AddRange(unknown);
        return known;
    }
}
