using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.DTOs;

public sealed record ReportQueryInput
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; }
    public string? Search { get; init; }
    public int? DepartmentId { get; init; }
    public int? RequestTypeId { get; init; }
    public string? Status { get; init; }
    public string? Priority { get; init; }
    public int? EmployeeUserId { get; init; }
    public int? ManagerUserId { get; init; }
}

public sealed record ReportFilter(
    DateOnly From,
    DateOnly To,
    DateTime FromUtc,
    DateTime ToUtcExclusive,
    int Page,
    int PageSize,
    string SortBy,
    bool SortDescending,
    string Search,
    int? DepartmentId,
    int? RequestTypeId,
    RequestStatus? Status,
    RequestPriority? Priority,
    int? EmployeeUserId,
    int? ManagerUserId);

public sealed record ReportPage<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record ReportActor(
    int UserId,
    string Role,
    int? DepartmentId);

public sealed record RequestReportRow(
    int RequestId,
    string Title,
    string Status,
    string? RequestTypeCode,
    string? RequestTypeName,
    string? Priority,
    DateTime CreatedAtUtc,
    int? EmployeeUserId,
    string? EmployeeName,
    int? DepartmentId,
    string? DepartmentName,
    decimal? Amount);

public sealed record ApprovalHistoryReportRow(
    long RecordId,
    string Source,
    int RequestId,
    string RequestTitle,
    string RequestStatus,
    int? EmployeeUserId,
    string? EmployeeName,
    string? DepartmentName,
    int ManagerUserId,
    string ManagerName,
    int? StageSequence,
    string Decision,
    DateTime AssignedAtUtc,
    DateTime DecidedAtUtc,
    string? Comment);

public sealed record WorkflowPerformanceReportRow(
    int RequestId,
    string RequestTitle,
    string WorkflowName,
    int WorkflowVersion,
    string WorkflowStatus,
    string RequestStatus,
    int StageCount,
    int ApprovedStageCount,
    int RejectedStageCount,
    int ActiveStageCount,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    double? ElapsedHours,
    int? EmployeeUserId,
    string? EmployeeName,
    string? DepartmentName);

public sealed record LeaveReportRow(
    int RequestId,
    string RequestStatus,
    string? Priority,
    DateTime CreatedAtUtc,
    int EmployeeUserId,
    string EmployeeName,
    int? DepartmentId,
    string? DepartmentName,
    int? LeaveTypeId,
    string LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalLeaveDays,
    decimal? CalculatedLeaveUnits,
    decimal? Earned,
    decimal? Used,
    decimal? Pending,
    decimal? Scheduled,
    decimal? Available);

public sealed record ReportFilterOptions(
    IReadOnlyList<ReportOption> Departments,
    IReadOnlyList<ReportOption> Employees,
    IReadOnlyList<ReportOption> Managers,
    IReadOnlyList<ReportOption> RequestTypes);

public sealed record ReportOption(int Id, string Name);
