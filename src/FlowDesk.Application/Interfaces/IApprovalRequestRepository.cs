using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public interface IApprovalRequestRepository
{
    Task<List<ApprovalRequest>> GetForEmployeeAsync(int userId);

    Task<List<ApprovalRequest>> GetAssignedToManagerAsync(int userId);

    Task<List<ApprovalRequest>> GetAllOwnedAsync();

    Task<List<ApprovalRequest>> GetLegacyAsync();

    Task<ApprovalRequest?> GetOwnedByIdAsync(int id, int userId);

    Task<ApprovalRequest?> GetAssignedToManagerByIdAsync(int id, int userId);

    Task<ApprovalRequest?> GetAdminVisibleByIdAsync(int id);

    Task<ApprovalRequest?> GetLegacyByIdAsync(int id);

    Task<ApprovalRequest?> GetByIdAsync(int id);

    Task AddAsync(ApprovalRequest request);

    Task<AssignmentCreationResult> TryAddPendingAssignmentAsync(
        int requestId,
        ApprovalAssignment assignment);

    Task<ApprovalActionResult> DecideAssignedRequestAsync(
        int requestId,
        int approverUserId,
        bool approve);

    Task<RequestCancellationResult> CancelOwnedRequestAsync(
        int requestId,
        int ownerUserId);

    Task SaveChangesAsync();
}