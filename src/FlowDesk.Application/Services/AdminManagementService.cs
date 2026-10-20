using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using System.Net.Mail;

namespace FlowDesk.Application.Services;

public class AdminManagementService
{
    private readonly IUserRepository _userRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPasswordHasher _passwordHasher;

    public AdminManagementService(
        IUserRepository userRepository,
        IDepartmentRepository departmentRepository,
        IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _departmentRepository = departmentRepository;
        _passwordHasher = passwordHasher;
    }

    public async Task<List<User>> GetUsersAsync(int adminUserId)
    {
        await RequireAdminAsync(adminUserId);
        return await _userRepository.GetAllAsync();
    }

    public async Task<User?> GetUserAsync(int adminUserId, int userId)
    {
        await RequireAdminAsync(adminUserId);
        ValidateUserId(userId);
        return await _userRepository.GetByIdAsync(userId);
    }

    public async Task<User?> UpdateUserRoleAsync(
        int adminUserId,
        int userId,
        string role)
    {
        await RequireAdminAsync(adminUserId);
        ValidateUserId(userId);
        var normalizedRole = UserRoles.Normalize(role);
        var user = await _userRepository.GetTrackedByIdAsync(userId);
        if (user == null)
        {
            return null;
        }

        if (user.Role == UserRoles.Manager &&
            normalizedRole != UserRoles.Manager &&
            await _userRepository.HasOpenApprovalWorkAsync(userId))
        {
            throw new InvalidOperationException(
                "A Manager with open approval work cannot be demoted.");
        }

        if (adminUserId == userId &&
            !string.Equals(user.Role, normalizedRole, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Administrators cannot change their own role.");
        }

        if (user.Role == UserRoles.Admin &&
            normalizedRole != UserRoles.Admin &&
            await _userRepository.CountByRoleAsync(UserRoles.Admin) <= 1)
        {
            throw new InvalidOperationException(
                "The last Administrator cannot be demoted.");
        }

        user.ChangeRole(normalizedRole);
        await _userRepository.SaveChangesAsync();
        return user;
    }

    public async Task<User?> UpdateUserActiveAsync(
        int adminUserId,
        int userId,
        bool isActive)
    {
        await RequireAdminAsync(adminUserId);
        ValidateUserId(userId);
        var user = await _userRepository.GetTrackedByIdAsync(userId);
        if (user == null)
        {
            return null;
        }

        if (adminUserId == userId && !isActive)
        {
            throw new InvalidOperationException(
                "Administrators cannot deactivate their own account.");
        }

        if (user.Role == UserRoles.Admin &&
            !isActive &&
            await _userRepository.CountByRoleAsync(UserRoles.Admin) <= 1)
        {
            throw new InvalidOperationException(
                "The last Administrator cannot be deactivated.");
        }

        if (user.Role == UserRoles.Manager &&
            !isActive &&
            await _userRepository.HasOpenApprovalWorkAsync(userId))
        {
            throw new InvalidOperationException(
                "A Manager with open approval work cannot be deactivated.");
        }

        user.SetActive(isActive);
        await _userRepository.SaveChangesAsync();
        return user;
    }

    public async Task<User> CreateUserAsync(
        int adminUserId,
        string fullName,
        string email,
        string password,
        string role,
        int departmentId)
    {
        await RequireAdminAsync(adminUserId);

        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > 100)
        {
            throw new ArgumentException(
                "Full name is required and cannot exceed 100 characters.",
                nameof(fullName));
        }

        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 150)
        {
            throw new ArgumentException(
                "A valid email address of at most 150 characters is required.",
                nameof(email));
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (!MailAddress.TryCreate(normalizedEmail, out var parsedEmail) ||
            !string.Equals(
                parsedEmail.Address,
                normalizedEmail,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid email address.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
        {
            throw new ArgumentException(
                "Password must contain at least 12 characters.",
                nameof(password));
        }

        var normalizedRole = UserRoles.Normalize(role);
        if (normalizedRole == UserRoles.Admin)
        {
            throw new ArgumentException(
                "Admin users cannot be created through this endpoint.",
                nameof(role));
        }

        var department = departmentId > 0
            ? await _departmentRepository.GetByIdAsync(departmentId)
            : null;
        if (department is null || !department.IsActive)
        {
            throw new ArgumentException(
                "Select an existing active department.",
                nameof(departmentId));
        }

        if (await _userRepository.GetByEmailAsync(normalizedEmail) != null)
        {
            throw new InvalidOperationException(
                "Email is already registered.");
        }

        var user = new User(
            fullName,
            normalizedEmail,
            _passwordHasher.Hash(password));
        user.ChangeRole(normalizedRole);
        user.ChangeDepartment(department.Id);
        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();
        return user;
    }

    public async Task<User?> UpdateUserDepartmentAsync(
        int adminUserId,
        int userId,
        int? departmentId)
    {
        await RequireAdminAsync(adminUserId);
        ValidateUserId(userId);

        if (departmentId.HasValue)
        {
            var department = departmentId.Value > 0
                ? await _departmentRepository.GetByIdAsync(
                    departmentId.Value)
                : null;
            if (department is null || !department.IsActive)
            {
                throw new ArgumentException(
                    "Select an existing active department.",
                    nameof(departmentId));
            }
        }

        var user = await _userRepository.GetTrackedByIdAsync(userId);
        if (user == null)
        {
            return null;
        }

        user.ChangeDepartment(departmentId);
        await _userRepository.SaveChangesAsync();
        return user;
    }

    public async Task<List<Department>> GetDepartmentsAsync(int adminUserId)
    {
        await RequireAdminAsync(adminUserId);
        return await _departmentRepository.GetAllAsync();
    }

    public async Task<Department> CreateDepartmentAsync(
        int adminUserId,
        string name,
        string? code)
    {
        await RequireAdminAsync(adminUserId);
        var department = new Department(name, code);
        await _departmentRepository.AddAsync(department);
        await _departmentRepository.SaveChangesAsync();
        return department;
    }

    private async Task RequireAdminAsync(int adminUserId)
    {
        var user = adminUserId > 0
            ? await _userRepository.GetByIdAsync(adminUserId)
            : null;

        if (user == null ||
            !string.Equals(
                user.Role,
                UserRoles.Admin,
                StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The authenticated user is not authorized for this operation.");
        }
    }

    private static void ValidateUserId(int userId)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(userId),
                "User ID must be greater than zero.");
        }
    }
}
