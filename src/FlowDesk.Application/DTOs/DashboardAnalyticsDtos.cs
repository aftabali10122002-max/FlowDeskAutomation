using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.DTOs;

public sealed record DashboardAnalyticsDto(
    string Role,
    DateOnly From,
    DateOnly To,
    int TotalRequests,
    IReadOnlyList<AnalyticsCountDto> StatusDistribution,
    IReadOnlyList<AnalyticsPointDto> SubmissionTrend,
    IReadOnlyList<DepartmentPerformanceDto> DepartmentPerformance,
    IReadOnlyList<DecisionTimeDto> ApprovalPerformance,
    IReadOnlyList<RequestTypeAnalyticsDto> RequestTypes,
    IReadOnlyList<AnalyticsPointDto> AssignedRequestTrend,
    IReadOnlyList<AnalyticsCountDto> ManagerRequestStatus,
    IReadOnlyList<AnalyticsPointDto> DecisionActivity,
    double? AverageDecisionTimeHours,
    IReadOnlyList<AnalyticsCountDto> PriorityDistribution,
    IReadOnlyList<AnalyticsCountDto> ApprovalProgress,
    AdminAnalyticsSummaryDto? AdminSummary,
    ManagerAnalyticsSummaryDto? ManagerSummary,
    EmployeeAnalyticsSummaryDto? EmployeeSummary,
    IReadOnlyList<WorkflowBottleneckDto> WorkflowBottlenecks);

public sealed record AdminAnalyticsSummaryDto(
    int TotalRequests,
    int PendingRequests,
    int ApprovedRequests,
    int RejectedRequests,
    int CancelledRequests,
    double? ApprovalRatePercent,
    double? AverageApprovalTimeHours,
    int OverdueRequests,
    int ActiveEmployees,
    int ActiveManagers);

public sealed record ManagerAnalyticsSummaryDto(
    int AssignedRequests,
    int PendingApprovals,
    int ApprovedRequests,
    int RejectedRequests,
    int OverdueApprovals,
    double? AverageDecisionTimeHours);

public sealed record EmployeeAnalyticsSummaryDto(
    int TotalRequests,
    int PendingRequests,
    int ApprovedRequests,
    int RejectedRequests,
    int CancelledRequests);

public sealed record WorkflowBottleneckDto(
    string Stage,
    int PendingApprovals,
    int OverdueApprovals,
    double AverageAgeHours);

public sealed record AnalyticsCountDto(string Label, int Count);

public sealed record AnalyticsPointDto(string Date, int Count);

public sealed record DepartmentPerformanceDto(
    string Department,
    int Approved,
    int Pending,
    int Rejected);

public sealed record DecisionTimeDto(
    string Department,
    double AverageHours,
    int Decisions);

public sealed record RequestTypeAnalyticsDto(
    string Code,
    string Name,
    int Count);

public sealed record DashboardUserAccessDto(string Role, bool IsActive);

public sealed record DashboardActiveUserCountsDto(
    int Employees,
    int Managers);

public sealed record DashboardRequestRecord(
    int Id,
    DateTime CreatedAtUtc,
    RequestStatus Status,
    string? Department,
    string? RequestTypeCode,
    string? RequestTypeName,
    RequestPriority? Priority,
    decimal? Amount,
    bool HasWorkflow);

public sealed record DashboardAssignmentRecord(
    int RequestId,
    DateTime AssignedAtUtc,
    ApprovalAssignmentStatus AssignmentStatus,
    RequestStatus RequestStatus,
    RequestPriority? Priority);

public sealed record DashboardDecisionRecord(
    DateTime DecidedAtUtc,
    DateTime? ActivatedAtUtc,
    string? Department,
    ApprovalWorkflowStageStatus Decision);

public sealed record DashboardPendingStageRecord(
    int Sequence,
    DateTime? ActivatedAtUtc,
    DateTime? DueAtUtc);
