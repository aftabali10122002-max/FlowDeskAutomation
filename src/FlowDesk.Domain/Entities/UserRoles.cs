namespace FlowDesk.Domain.Entities;

public static class UserRoles
{
    public const string Employee = "Employee";
    public const string Manager = "Manager";
    public const string Admin = "Admin";

    public static string Normalize(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Role is required.", nameof(role));
        }

        var normalizedRole = role.Trim();
        if (string.Equals(
                normalizedRole,
                Employee,
                StringComparison.OrdinalIgnoreCase))
        {
            return Employee;
        }

        if (string.Equals(
                normalizedRole,
                Manager,
                StringComparison.OrdinalIgnoreCase))
        {
            return Manager;
        }

        if (string.Equals(
                normalizedRole,
                Admin,
                StringComparison.OrdinalIgnoreCase))
        {
            return Admin;
        }

        throw new ArgumentException(
            "Role must be Employee, Manager, or Admin.",
            nameof(role));
    }
}
