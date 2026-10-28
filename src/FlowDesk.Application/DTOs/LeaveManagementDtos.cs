using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.DTOs;

public sealed record LeavePolicyEntitlementInput(
    int LeaveTypeId,
    decimal AnnualEntitlement);

public sealed record LeavePolicyDraftInput(
    string Code,
    string Name,
    int Version,
    LeavePolicyScope Scope,
    int? DepartmentId,
    int? DesignationId,
    EmploymentType? EmploymentType,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    LeaveDayCalculationMethod DayCalculationMethod,
    int? LeaveWorkScheduleId,
    int? LeaveHolidayCalendarId,
    bool AllowsHalfDays,
    decimal? HalfDayUnits,
    bool IsAccrualEnabled,
    LeaveAccrualFrequency? AccrualFrequency,
    bool AllowsCarryForward,
    decimal? CarryForwardCap,
    int? CarryForwardExpiryMonths,
    int? ProbationPeriodDays,
    bool AllowsLeaveDuringProbation,
    bool AllowsNegativeBalance,
    int? LeavePayrollCalendarId,
    IReadOnlyList<LeavePolicyEntitlementInput> Entitlements);

public sealed record LeavePolicyEntitlementResponse(
    int LeaveTypeId,
    decimal AnnualEntitlement);

public sealed record LeavePolicyResponse(
    int Id,
    string Code,
    string Name,
    int Version,
    LeavePolicyStatus Status,
    LeavePolicyScope Scope,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    IReadOnlyList<LeavePolicyEntitlementResponse> Entitlements);

public sealed record EmployeeEmploymentProfileInput(
    int EmployeeUserId,
    int? DesignationId,
    EmploymentType? EmploymentType,
    DateOnly? HireDate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record LeavePolicyAssignmentInput(
    int EmployeeUserId,
    int LeavePolicyId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record LeavePolicyResolutionData(
    FlowDesk.Domain.Entities.User Employee,
    IReadOnlyList<FlowDesk.Domain.Entities.EmployeeEmploymentProfile>
        EmploymentProfiles,
    IReadOnlyList<FlowDesk.Domain.Entities.LeavePolicyAssignment> Assignments,
    IReadOnlyList<FlowDesk.Domain.Entities.LeavePolicy> Policies,
    IReadOnlyDictionary<int, IReadOnlySet<DayOfWeek>> WorkingDaysBySchedule,
    IReadOnlyDictionary<int, IReadOnlySet<DateOnly>> HolidaysByCalendar);

public sealed record EmployeeLeaveMetadata(
    int EmployeeUserId,
    string FullName,
    string Role,
    int? DepartmentId,
    string? DepartmentName,
    int? DesignationId,
    string? DesignationName,
    EmploymentType? EmploymentType,
    DateOnly? HireDate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record LeaveRequestPreviewInput(
    string LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    FlowDesk.Domain.Enums.LeaveDayPortion StartDayPortion,
    FlowDesk.Domain.Enums.LeaveDayPortion EndDayPortion);

public sealed record LeaveRequestPreviewDay(
    DateOnly Date,
    decimal Units,
    int PolicyId,
    int PolicyVersion);

public sealed record LeaveRequestPreview(
    decimal TotalUnits,
    IReadOnlyList<LeaveRequestPreviewDay> Days);
