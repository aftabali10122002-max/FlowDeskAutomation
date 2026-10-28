using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email);

    Task<User?> GetByIdAsync(int id);

    Task<User?> GetProfileByIdAsync(int id);

    Task<User?> GetTrackedByIdAsync(int id);

    Task<List<User>> GetAllAsync();
    Task<List<User>> GetActiveByDepartmentAsync(int departmentId);

    Task<int> CountByRoleAsync(string role);

    Task<bool> HasOpenApprovalWorkAsync(int userId);

    Task AddAsync(User user);

    Task SaveChangesAsync();
}