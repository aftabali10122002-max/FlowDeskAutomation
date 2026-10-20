namespace FlowDesk.Domain.Entities;

public class Department
{
    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Code { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTime CreatedAtUtc { get; private set; }

    public ICollection<User> Users { get; private set; } = new List<User>();

    public ICollection<ApprovalRequest> ApprovalRequests { get; private set; }
        = new List<ApprovalRequest>();

    private Department() { }

    public Department(string name, string? code = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Department name is required.", nameof(name));
        }

        var normalizedName = name.Trim();
        if (normalizedName.Length > 100)
        {
            throw new ArgumentException(
                "Department name cannot exceed 100 characters.",
                nameof(name));
        }

        var normalizedCode = string.IsNullOrWhiteSpace(code)
            ? null
            : code.Trim().ToUpperInvariant();
        if (normalizedCode?.Length > 20)
        {
            throw new ArgumentException(
                "Department code cannot exceed 20 characters.",
                nameof(code));
        }

        Name = normalizedName;
        Code = normalizedCode;
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
