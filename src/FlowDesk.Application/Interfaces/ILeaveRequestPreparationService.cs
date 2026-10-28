using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public interface ILeaveRequestPreparationService
{
    Task PrepareAsync(ApprovalRequest request);
    Task<FlowDesk.Application.DTOs.LeaveRequestPreview> PreviewAsync(
        int employeeUserId,
        FlowDesk.Application.DTOs.LeaveRequestPreviewInput input);
}
