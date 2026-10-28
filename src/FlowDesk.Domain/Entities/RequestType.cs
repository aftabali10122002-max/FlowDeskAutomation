namespace FlowDesk.Domain.Entities;

public static class RequestTypeCodes
{
    public const string MaterialPurchase = "MATERIAL_PURCHASE";
    public const string Leave = "LEAVE";
    public const string ExpenseReimbursement = "EXPENSE_REIMBURSEMENT";
    public const string ItAccess = "IT_ACCESS";
    public const string Travel = "TRAVEL";
    public const string WorkFromHome = "WORK_FROM_HOME";
}

public sealed class RequestType
{
    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public bool RequiresAmount { get; private set; }
    public bool IsEnabled { get; private set; } = true;

    private RequestType() { }

    public RequestType(
        string code,
        string displayName,
        bool requiresAmount)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 50)
        {
            throw new ArgumentException(
                "A request type code is required and cannot exceed 50 characters.",
                nameof(code));
        }

        if (string.IsNullOrWhiteSpace(displayName) ||
            displayName.Trim().Length > 100)
        {
            throw new ArgumentException(
                "A request type display name is required and cannot exceed 100 characters.",
                nameof(displayName));
        }

        Code = code.Trim().ToUpperInvariant();
        DisplayName = displayName.Trim();
        RequiresAmount = requiresAmount;
        IsEnabled = true;
    }

    public void SetEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }
}
