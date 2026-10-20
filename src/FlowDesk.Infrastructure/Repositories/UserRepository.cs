using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Email == email);
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<User?> GetTrackedByIdAsync(int id)
    {
        return await _context.Users
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public Task<List<User>> GetAllAsync()
    {
        return _context.Users
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .ToListAsync();
    }

    public Task<int> CountByRoleAsync(string role)
    {
        return _context.Users.CountAsync(x => x.Role == role);
    }

    public async Task<bool> HasOpenApprovalWorkAsync(int userId)
    {
        var hasPendingAssignment = await _context.ApprovalAssignments.AnyAsync(x =>
            x.ApproverUserId == userId &&
            x.Status == FlowDesk.Domain.Enums.ApprovalAssignmentStatus.Pending &&
            x.Request.Status == FlowDesk.Domain.Enums.RequestStatus.Pending);
        if (hasPendingAssignment)
        {
            return true;
        }

        return await _context.ApprovalWorkflowStages.AnyAsync(x =>
            x.ApproverUserId == userId &&
            (x.Status == FlowDesk.Domain.Enums.ApprovalWorkflowStageStatus.Active ||
             x.Status == FlowDesk.Domain.Enums.ApprovalWorkflowStageStatus.Inactive) &&
            x.WorkflowInstance.Status ==
                FlowDesk.Domain.Enums.ApprovalWorkflowStatus.Active);
    }

    public async Task AddAsync(User user)
    {
        await _context.Users.AddAsync(user);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}