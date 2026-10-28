using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Repositories;

public sealed class DashboardAnalyticsRepository : IDashboardAnalyticsRepository
{
    private readonly AppDbContext _context;

    public DashboardAnalyticsRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<DashboardUserAccessDto?> GetUserAccessAsync(int userId)
    {
        return _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new DashboardUserAccessDto(
                user.Role,
                user.IsActive))
            .SingleOrDefaultAsync();
    }

    public async Task<DashboardActiveUserCountsDto> GetActiveUserCountsAsync()
    {
        var users = await _context.Users
            .AsNoTracking()
            .Where(user => user.IsActive &&
                (user.Role == UserRoles.Employee ||
                 user.Role == UserRoles.Manager))
            .GroupBy(user => user.Role)
            .Select(group => new
            {
                Role = group.Key,
                Count = group.Count()
            })
            .ToListAsync();

        return new DashboardActiveUserCountsDto(
            users.SingleOrDefault(user => user.Role == UserRoles.Employee)
                ?.Count ?? 0,
            users.SingleOrDefault(user => user.Role == UserRoles.Manager)
                ?.Count ?? 0);
    }

    public async Task<IReadOnlyList<DashboardRequestRecord>> GetRequestsAsync(
        int? ownerUserId,
        bool includeLegacy,
        DateTime fromUtc,
        DateTime toUtcExclusive)
    {
        var query = _context.ApprovalRequests
            .AsNoTracking()
            .Where(request =>
                request.CreatedAtUtc >= fromUtc &&
                request.CreatedAtUtc < toUtcExclusive);

        if (ownerUserId.HasValue)
        {
            query = query.Where(request =>
                request.CreatedByUserId == ownerUserId.Value);
        }
        else if (!includeLegacy)
        {
            query = query.Where(request =>
                request.CreatedByUserId != null);
        }

        return await query.Select(ToRequestRecord()).ToListAsync();
    }

    public async Task<IReadOnlyList<DashboardRequestRecord>> GetManagerRequestsAsync(
        int managerUserId,
        DateTime fromUtc,
        DateTime toUtcExclusive)
    {
        return await _context.ApprovalRequests
            .AsNoTracking()
            .Where(request =>
                request.CreatedByUserId != null &&
                request.CreatedAtUtc >= fromUtc &&
                request.CreatedAtUtc < toUtcExclusive &&
                request.Assignments.Any(assignment =>
                    assignment.ApproverUserId == managerUserId))
            .Select(ToRequestRecord())
            .ToListAsync();
    }

    public async Task<IReadOnlyList<DashboardAssignmentRecord>>
        GetManagerAssignmentsAsync(
            int managerUserId,
            DateTime fromUtc,
            DateTime toUtcExclusive)
    {
        return await _context.ApprovalAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.ApproverUserId == managerUserId &&
                assignment.Request.CreatedByUserId != null &&
                assignment.AssignedAtUtc >= fromUtc &&
                assignment.AssignedAtUtc < toUtcExclusive)
            .Select(assignment => new DashboardAssignmentRecord(
                assignment.RequestId,
                assignment.AssignedAtUtc,
                assignment.Status,
                assignment.Request.Status,
                assignment.Request.Priority))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<DashboardDecisionRecord>> GetDecisionsAsync(
        int? managerUserId,
        DateTime fromUtc,
        DateTime toUtcExclusive)
    {
        var decisions = _context.ApprovalDecisionHistory
            .AsNoTracking()
            .Where(history =>
                history.DecidedAtUtc >= fromUtc &&
                history.DecidedAtUtc < toUtcExclusive);

        if (managerUserId.HasValue)
        {
            decisions = decisions.Where(history =>
                history.ManagerUserId == managerUserId.Value &&
                history.WorkflowStage.WorkflowInstance.Request
                    .CreatedByUserId != null);
        }

        var workflowDecisions = await decisions
            .Select(history => new DashboardDecisionRecord(
                history.DecidedAtUtc,
                history.WorkflowStage.ActivatedAtUtc,
                history.WorkflowStage.WorkflowInstance.Request.Department == null
                    ? null
                    : history.WorkflowStage.WorkflowInstance.Request
                        .Department!.Name,
                history.Decision))
            .ToListAsync();

        var manualAssignments = _context.ApprovalAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.WorkflowStageId == null &&
                assignment.DecidedAtUtc >= fromUtc &&
                assignment.DecidedAtUtc < toUtcExclusive &&
                assignment.Request.CreatedByUserId != null &&
                (assignment.Status == ApprovalAssignmentStatus.Approved ||
                 assignment.Status == ApprovalAssignmentStatus.Rejected));
        if (managerUserId.HasValue)
        {
            manualAssignments = manualAssignments.Where(assignment =>
                assignment.ApproverUserId == managerUserId.Value);
        }

        var manualDecisions = await manualAssignments
            .Select(assignment => new DashboardDecisionRecord(
                assignment.DecidedAtUtc!.Value,
                assignment.AssignedAtUtc,
                assignment.Request.Department == null
                    ? null
                    : assignment.Request.Department!.Name,
                assignment.Status == ApprovalAssignmentStatus.Approved
                    ? ApprovalWorkflowStageStatus.Approved
                    : ApprovalWorkflowStageStatus.Rejected))
            .ToListAsync();

        return workflowDecisions.Concat(manualDecisions).ToArray();
    }

    public async Task<IReadOnlyList<DashboardPendingStageRecord>>
        GetPendingWorkflowStagesAsync(
            int? managerUserId,
            DateTime fromUtc,
            DateTime toUtcExclusive,
            DateTime asOfUtc)
    {
        var stages = _context.ApprovalWorkflowStages
            .AsNoTracking()
            .Where(stage =>
                stage.Status == ApprovalWorkflowStageStatus.Active &&
                stage.WorkflowInstance.Request.CreatedByUserId != null &&
                stage.WorkflowInstance.Request.CreatedAtUtc >= fromUtc &&
                stage.WorkflowInstance.Request.CreatedAtUtc < toUtcExclusive);

        if (managerUserId.HasValue)
        {
            stages = stages.Where(stage =>
                stage.ApproverUserId == managerUserId.Value);
        }

        return await stages
            .Select(stage => new DashboardPendingStageRecord(
                stage.Sequence,
                stage.ActivatedAtUtc,
                stage.DueAtUtc))
            .ToListAsync();
    }

    private static System.Linq.Expressions.Expression<
        Func<ApprovalRequest, DashboardRequestRecord>> ToRequestRecord()
    {
        return request => new DashboardRequestRecord(
            request.Id,
            request.CreatedAtUtc,
            request.Status,
            request.Department == null ? null : request.Department.Name,
            request.RequestType == null ? null : request.RequestType.Code,
            request.RequestType == null
                ? null
                : request.RequestType.DisplayName,
            request.Priority,
            request.Amount,
            request.WorkflowInstance != null);
    }
}
