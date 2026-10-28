namespace FlowDesk.Application.DTOs;

public sealed record LeaveScheduleDayInput(
    DayOfWeek DayOfWeek,
    bool IsWorkingDay,
    decimal StandardHours);

public sealed record LeaveScheduleInput(
    string Code,
    string Name,
    IReadOnlyList<LeaveScheduleDayInput> Days);

public sealed record LeaveHolidayCalendarInput(
    string Code,
    string Name,
    string? Region);

public sealed record LeaveHolidayInput(
    DateOnly Date,
    string Name);

public sealed record LeavePayrollCalendarInput(
    string Code,
    string Name,
    string TimeZoneId,
    int ExpectedPeriodsInEntitlementYear);

public sealed record LeavePayPeriodInput(
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly PostingDate,
    int EntitlementYear,
    int PeriodNumber);

public sealed record DesignationInput(string Code, string Name);
