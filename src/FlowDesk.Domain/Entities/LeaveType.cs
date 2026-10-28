namespace FlowDesk.Domain.Entities;

public sealed class LeaveType
{
    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public bool IsPaid { get; private set; }
    public bool IsEnabled { get; private set; } = true;

    private LeaveType() { }

    public LeaveType(string code, string displayName, bool isPaid)
    {
        Code = RequireText(code, 50, nameof(code)).ToUpperInvariant();
        DisplayName = RequireText(displayName, 100, nameof(displayName));
        IsPaid = isPaid;
        IsEnabled = true;
    }

    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;

    public void Update(string displayName, bool isPaid, bool isEnabled)
    {
        DisplayName = RequireText(displayName, 100, nameof(displayName));
        IsPaid = isPaid;
        IsEnabled = isEnabled;
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
