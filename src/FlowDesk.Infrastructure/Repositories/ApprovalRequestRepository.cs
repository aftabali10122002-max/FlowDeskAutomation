using System.Data;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Repositories;

public class ApprovalRequestRepository : IApprovalRequestRepository
{
    private readonly AppDbContext _context;

    public ApprovalRequestRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<List<ApprovalRequest>> GetForEmployeeAsync(int userId)
    {
        return _context.ApprovalRequests
            .AsNoTracking()
            .Include(x => x.CreatedByUser)
            .Include(x => x.Department)
            .Include(x => x.Assignments)
                .ThenInclude(a => a.ApproverUser)
            .Where(x => x.CreatedByUserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();
    }

    public Task<List<ApprovalRequest>> GetAssignedToManagerAsync(int userId)
    {
        return _context.ApprovalRequests
            .AsNoTracking()
            .Include(x => x.CreatedByUser)
            .Include(x => x.Department)
            .Include(x => x.Assignments
                .Where(a => a.ApproverUserId == userId))
                .ThenInclude(a => a.ApproverUser)
            .Where(x =>
                x.CreatedByUserId != null &&
                x.Assignments.Any(a => a.ApproverUserId == userId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();
    }

    public Task<List<ApprovalRequest>> GetAllOwnedAsync()
    {
        return _context.ApprovalRequests
            .AsNoTracking()
            .Include(x => x.CreatedByUser)
            .Include(x => x.Department)
            .Include(x => x.Assignments)
                .ThenInclude(a => a.ApproverUser)
            .Where(x => x.CreatedByUserId != null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();
    }

    public Task<List<ApprovalRequest>> GetLegacyAsync()
    {
        return _context.ApprovalRequests
            .AsNoTracking()
            .Where(x => x.CreatedByUserId == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();
    }

    public Task<ApprovalRequest?> GetOwnedByIdAsync(int id, int userId)
    {
        return _context.ApprovalRequests
            .AsNoTracking()
            .Include(x => x.CreatedByUser)
            .Include(x => x.Department)
            .Include(x => x.Assignments)
                .ThenInclude(a => a.ApproverUser)
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.CreatedByUserId == userId);
    }

    public Task<ApprovalRequest?> GetAssignedToManagerByIdAsync(
        int id,
        int userId)
    {
        return _context.ApprovalRequests
            .AsNoTracking()
            .Include(x => x.CreatedByUser)
            .Include(x => x.Department)
            .Include(x => x.Assignments
                .Where(a => a.ApproverUserId == userId))
                .ThenInclude(a => a.ApproverUser)
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.CreatedByUserId != null &&
                x.Assignments.Any(a => a.ApproverUserId == userId));
    }

    public Task<ApprovalRequest?> GetAdminVisibleByIdAsync(int id)
    {
        return _context.ApprovalRequests
            .AsNoTracking()
            .Include(x => x.CreatedByUser)
            .Include(x => x.Department)
            .Include(x => x.Assignments)
                .ThenInclude(a => a.ApproverUser)
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.CreatedByUserId != null);
    }

    public Task<ApprovalRequest?> GetLegacyByIdAsync(int id)
    {
        return _context.ApprovalRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.CreatedByUserId == null);
    }

    public Task<ApprovalRequest?> GetByIdAsync(int id)
    {
        return _context.ApprovalRequests
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task AddAsync(ApprovalRequest request)
    {
        await _context.ApprovalRequests.AddAsync(request);
    }

    public async Task<AssignmentCreationResult> TryAddPendingAssignmentAsync(
        int requestId,
        ApprovalAssignment assignment)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);

        var isAssignable = await _context.ApprovalRequests
            .AnyAsync(x =>
                x.Id == requestId &&
                x.CreatedByUserId != null &&
                x.Status == RequestStatus.Pending);

        if (!isAssignable)
        {
            var requestExists = await _context.ApprovalRequests
                .AnyAsync(x => x.Id == requestId);
            return requestExists
                ? AssignmentCreationResult.Conflict
                : AssignmentCreationResult.RequestNotFound;
        }

        var hasPendingAssignment = await _context.ApprovalAssignments
            .AnyAsync(x =>
                x.RequestId == requestId &&
                x.Status == ApprovalAssignmentStatus.Pending);

        if (hasPendingAssignment)
        {
            return AssignmentCreationResult.Conflict;
        }

        await _context.ApprovalAssignments.AddAsync(assignment);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return AssignmentCreationResult.Created;
    }

    public async Task<ApprovalActionResult> DecideAssignedRequestAsync(
        int requestId,
        int approverUserId,
        bool approve)
    {
        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        var decidedAtUtc = DateTime.UtcNow;
        var assignmentStatus = approve
            ? ApprovalAssignmentStatus.Approved
            : ApprovalAssignmentStatus.Rejected;
        var requestStatus = approve
            ? RequestStatus.Approved
            : RequestStatus.Rejected;

        var requestRows = await _context.ApprovalRequests
            .Where(x =>
                x.Id == requestId &&
                x.CreatedByUserId != null &&
                x.Status == RequestStatus.Pending &&
                x.Assignments.Any(a =>
                    a.ApproverUserId == approverUserId &&
                    a.Status == ApprovalAssignmentStatus.Pending))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, requestStatus));

        if (requestRows == 0)
        {
            var hasAssignment = await _context.ApprovalAssignments
                .AnyAsync(x =>
                    x.RequestId == requestId &&
                    x.ApproverUserId == approverUserId);

            return hasAssignment
                ? ApprovalActionResult.Conflict
                : ApprovalActionResult.NotAssigned;
        }

        var assignmentRows = await _context.ApprovalAssignments
            .Where(x =>
                x.RequestId == requestId &&
                x.ApproverUserId == approverUserId &&
                x.Status == ApprovalAssignmentStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, assignmentStatus)
                .SetProperty(x => x.DecidedAtUtc, decidedAtUtc));

        if (assignmentRows == 0)
        {
            await transaction.RollbackAsync();
            return ApprovalActionResult.Conflict;
        }

        await transaction.CommitAsync();
        return ApprovalActionResult.Success;
    }

    public async Task<RequestCancellationResult> CancelOwnedRequestAsync(
        int requestId,
        int ownerUserId)
    {
        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        var decidedAtUtc = DateTime.UtcNow;
        var updatedRows = await _context.ApprovalRequests
            .Where(x =>
                x.Id == requestId &&
                x.CreatedByUserId == ownerUserId &&
                x.Status == RequestStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, RequestStatus.Cancelled));

        if (updatedRows == 1)
        {
            await _context.ApprovalAssignments
                .Where(x =>
                    x.RequestId == requestId &&
                    x.Status == ApprovalAssignmentStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        x => x.Status,
                        ApprovalAssignmentStatus.Cancelled)
                    .SetProperty(x => x.DecidedAtUtc, decidedAtUtc));

            await transaction.CommitAsync();
            return RequestCancellationResult.Success;
        }

        var isOwned = await _context.ApprovalRequests
            .AnyAsync(x =>
                x.Id == requestId &&
                x.CreatedByUserId == ownerUserId);

        return isOwned
            ? RequestCancellationResult.Conflict
            : RequestCancellationResult.NotFound;
    }

    public Task SaveChangesAsync()
    {
        return _context.SaveChangesAsync();
    }
}
