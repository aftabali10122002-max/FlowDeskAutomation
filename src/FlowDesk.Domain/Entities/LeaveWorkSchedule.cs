namespace FlowDesk.Domain.Entities;

public sealed class LeaveWorkSchedule
{
    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; } = true;
    public ICollection<LeaveWorkScheduleDay> Days { get; private set; } = [];

    private LeaveWorkSchedule() { }

    public LeaveWorkSchedule(string code, string name)
    {
        Code = RequireText(code, 50, nameof(code)).ToUpperInvariant();
        Name = RequireText(name, 100, nameof(name));
        IsEnabled = true;
    }

    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;

    public void AddDay(
        DayOfWeek dayOfWeek,
        bool isWorkingDay,
        decimal standardHours)
    {
        if (Days.Any(day => day.DayOfWeek == dayOfWeek))
        {
            throw new ArgumentException(
                "A weekday can only be configured once.",
                nameof(dayOfWeek));
        }

        var day = new LeaveWorkScheduleDay(
            0,
            dayOfWeek,
            isWorkingDay,
            standardHours);
        day.AssignSchedule(this);
        Days.Add(day);
    }

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

public sealed class LeaveWorkScheduleDay
{
    public int Id { get; private set; }
    public int LeaveWorkScheduleId { get; private set; }
    public LeaveWorkSchedule LeaveWorkSchedule { get; private set; } = null!;
    public DayOfWeek DayOfWeek { get; private set; }
    public bool IsWorkingDay { get; private set; }
    public decimal StandardHours { get; private set; }

    private LeaveWorkScheduleDay() { }

    public LeaveWorkScheduleDay(
        int leaveWorkScheduleId,
        DayOfWeek dayOfWeek,
        bool isWorkingDay,
        decimal standardHours)
    {
        if (leaveWorkScheduleId < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaveWorkScheduleId));
        }

        if (!Enum.IsDefined(dayOfWeek))
        {
            throw new ArgumentOutOfRangeException(nameof(dayOfWeek));
        }

        if (standardHours < 0 || standardHours > 24 ||
            (isWorkingDay && standardHours <= 0) ||
            (!isWorkingDay && standardHours != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(standardHours));
        }

        LeaveWorkScheduleId = leaveWorkScheduleId;
        DayOfWeek = dayOfWeek;
        IsWorkingDay = isWorkingDay;
        StandardHours = standardHours;
    }

    internal void AssignSchedule(LeaveWorkSchedule schedule)
    {
        LeaveWorkSchedule = schedule;
        LeaveWorkScheduleId = schedule.Id;
    }
}
