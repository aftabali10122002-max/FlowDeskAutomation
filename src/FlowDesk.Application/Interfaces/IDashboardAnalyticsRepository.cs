using FlowDesk.Application.DTOs;

namespace FlowDesk.Application.Interfaces;

public interface IDashboardAnalyticsRepository
{
    Task<DashboardUserAccessDto?> GetUserAccessAsync(int userId);

    Task<DashboardActiveUserCountsDto> GetActiveUserCountsAsync();

    Task<IReadOnlyList<DashboardRequestRecord>> GetRequestsAsync(
        int? ownerUserId,
        bool includeLegacy,
        DateTime fromUtc,
        DateTime toUtcExclusive);

    Task<IReadOnlyList<DashboardRequestRecord>> GetManagerRequestsAsync(
        int managerUserId,
        DateTime fromUtc,
        DateTime toUtcExclusive);

    Task<IReadOnlyList<DashboardAssignmentRecord>>
        GetManagerAssignmentsAsync(
            int managerUserId,
            DateTime fromUtc,
            DateTime toUtcExclusive);

    Task<IReadOnlyList<DashboardDecisionRecord>> GetDecisionsAsync(
        int? managerUserId,
        DateTime fromUtc,
        DateTime toUtcExclusive);

    Task<IReadOnlyList<DashboardPendingStageRecord>>
        GetPendingWorkflowStagesAsync(
            int? managerUserId,
            DateTime fromUtc,
            DateTime toUtcExclusive,
            DateTime asOfUtc);
}
