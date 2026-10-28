namespace FlowDesk.Domain.Entities;

public sealed class Designation
{
    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; } = true;

    private Designation() { }

    public Designation(string code, string name)
    {
        Code = RequireText(code, 50, nameof(code)).ToUpperInvariant();
        Name = RequireText(name, 100, nameof(name));
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
