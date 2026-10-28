namespace FlowDesk.Domain.Entities;

public sealed class LeaveHolidayCalendar
{
    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Region { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    public ICollection<LeaveHoliday> Holidays { get; private set; } = [];

    private LeaveHolidayCalendar() { }

    public LeaveHolidayCalendar(string code, string name, string? region = null)
    {
        Code = RequireText(code, 50, nameof(code)).ToUpperInvariant();
        Name = RequireText(name, 100, nameof(name));
        if (region?.Trim().Length > 100)
        {
            throw new ArgumentException(
                "Region cannot exceed 100 characters.",
                nameof(region));
        }

        Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
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

public sealed class LeaveHoliday
{
    public int Id { get; private set; }
    public int LeaveHolidayCalendarId { get; private set; }
    public LeaveHolidayCalendar LeaveHolidayCalendar { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string Name { get; private set; } = string.Empty;

    private LeaveHoliday() { }

    public LeaveHoliday(
        int leaveHolidayCalendarId,
        DateOnly date,
        string name)
    {
        if (leaveHolidayCalendarId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaveHolidayCalendarId));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150)
        {
            throw new ArgumentException(
                "Holiday name is required and cannot exceed 150 characters.",
                nameof(name));
        }

        LeaveHolidayCalendarId = leaveHolidayCalendarId;
        Date = date;
        Name = name.Trim();
    }
}
