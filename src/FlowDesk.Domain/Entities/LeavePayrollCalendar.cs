namespace FlowDesk.Domain.Entities;

public sealed class LeavePayrollCalendar
{
    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string TimeZoneId { get; private set; } = string.Empty;
    public int ExpectedPeriodsInEntitlementYear { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    public ICollection<LeavePayPeriod> Periods { get; private set; } = [];

    private LeavePayrollCalendar() { }

    public LeavePayrollCalendar(
        string code,
        string name,
        string timeZoneId,
        int expectedPeriodsInEntitlementYear)
    {
        Code = RequireText(code, 50, nameof(code)).ToUpperInvariant();
        Name = RequireText(name, 100, nameof(name));
        TimeZoneId = RequireText(timeZoneId, 100, nameof(timeZoneId));
        if (expectedPeriodsInEntitlementYear <= 0 ||
            expectedPeriodsInEntitlementYear > 366)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedPeriodsInEntitlementYear));
        }

        ExpectedPeriodsInEntitlementYear = expectedPeriodsInEntitlementYear;
        IsEnabled = true;
    }

    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;

    private static string RequireText(
        string value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Trim().Length > maximumLength)
        {
            throw new ArgumentException(
                $"A value is required and cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return value.Trim();
    }
}

public sealed class LeavePayPeriod
{
    public long Id { get; private set; }
    public int LeavePayrollCalendarId { get; private set; }
    public LeavePayrollCalendar LeavePayrollCalendar { get; private set; } =
        null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public DateOnly PostingDate { get; private set; }
    public int EntitlementYear { get; private set; }
    public int PeriodNumber { get; private set; }

    private LeavePayPeriod() { }

    public LeavePayPeriod(
        int leavePayrollCalendarId,
        DateOnly startDate,
        DateOnly endDate,
        DateOnly postingDate,
        int entitlementYear,
        int periodNumber)
    {
        if (leavePayrollCalendarId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leavePayrollCalendarId));
        }

        if (endDate < startDate ||
            postingDate < endDate ||
            entitlementYear < 1 ||
            periodNumber < 1)
        {
            throw new ArgumentException(
                "The payroll period dates and period identifiers are invalid.");
        }

        LeavePayrollCalendarId = leavePayrollCalendarId;
        StartDate = startDate;
        EndDate = endDate;
        PostingDate = postingDate;
        EntitlementYear = entitlementYear;
        PeriodNumber = periodNumber;
    }
}
