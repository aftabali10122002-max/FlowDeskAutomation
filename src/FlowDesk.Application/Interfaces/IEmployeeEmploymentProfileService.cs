using FlowDesk.Application.DTOs;

namespace FlowDesk.Application.Interfaces;

public interface IEmployeeEmploymentProfileService
{
    Task<IReadOnlyList<EmployeeLeaveMetadata>> GetMetadataAsync(
        int adminUserId,
        int employeeUserId,
        DateOnly asOfDate);
    Task AssignMetadataAsync(
        int adminUserId,
        EmployeeEmploymentProfileInput input);
}
