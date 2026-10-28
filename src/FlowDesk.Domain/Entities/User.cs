namespace FlowDesk.Domain.Entities;

public class User
{
    public int Id { get; private set; }

    public string FullName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public string? PhoneNumber { get; private set; }

    public string? ProfileImageStorageKey { get; private set; }

    public string Role { get; private set; } = UserRoles.Employee;

    public int? DepartmentId { get; private set; }

    public Department? Department { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTime CreatedAtUtc { get; private set; }

    public ICollection<ApprovalRequest> CreatedRequests { get; private set; }
        = new List<ApprovalRequest>();

    public ICollection<ApprovalAssignment> ApprovalAssignments { get; private set; }
        = new List<ApprovalAssignment>();

    private User()
    {
        // Required by EF Core
    }

    public User(
        string fullName,
        string email,
        string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name is required.");

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.");

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.");

        FullName = fullName.Trim();
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;
        Role = UserRoles.Employee;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void ChangeRole(string role)
    {
        Role = UserRoles.Normalize(role);
    }

    public void ChangePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException(
                "Password hash is required.",
                nameof(passwordHash));
        }

        PasswordHash = passwordHash;
    }

    public void UpdateProfile(string fullName, string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > 100)
        {
            throw new ArgumentException(
                "Full name is required and cannot exceed 100 characters.",
                nameof(fullName));
        }

        var normalizedPhoneNumber = string.IsNullOrWhiteSpace(phoneNumber)
            ? null
            : phoneNumber.Trim();
        if (normalizedPhoneNumber?.Length > 32)
        {
            throw new ArgumentException(
                "Phone number cannot exceed 32 characters.",
                nameof(phoneNumber));
        }

        FullName = fullName.Trim();
        PhoneNumber = normalizedPhoneNumber;
    }

    public void SetProfileImageStorageKey(string? storageKey)
    {
        if (storageKey is not null &&
            !Guid.TryParseExact(storageKey, "N", out _))
        {
            throw new ArgumentException(
                "Profile image storage key is invalid.",
                nameof(storageKey));
        }

        ProfileImageStorageKey = storageKey;
    }

    public void ChangeDepartment(int? departmentId)
    {
        if (departmentId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(departmentId));
        }

        DepartmentId = departmentId;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}