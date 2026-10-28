using FlowDesk.Application.DTOs;
using FlowDesk.Application.Errors;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Services;

public sealed class EmployeeEmploymentProfileService(
    ILeaveManagementRepository leaveRepository,
    IUserRepository userRepository) : IEmployeeEmploymentProfileService
{
    public async Task<IReadOnlyList<EmployeeLeaveMetadata>> GetMetadataAsync(
        int adminUserId,
        int employeeUserId,
        DateOnly asOfDate)
    {
        await RequireAdminAsync(adminUserId);
        if (employeeUserId <= 0)
        {
            throw new LeaveInvalidInputException(
                "A valid employee is required.");
        }

        return await leaveRepository.GetEmployeeMetadataAsync(
            employeeUserId,
            asOfDate);
    }

    public async Task AssignMetadataAsync(
        int adminUserId,
        EmployeeEmploymentProfileInput input)
    {
        await RequireAdminAsync(adminUserId);
        if (input.EmployeeUserId <= 0 ||
            input.DesignationId is <= 0 ||
            (input.EmploymentType.HasValue &&
             !Enum.IsDefined(input.EmploymentType.Value)) ||
            input.EffectiveTo < input.EffectiveFrom ||
            (input.HireDate.HasValue &&
             input.HireDate.Value > input.EffectiveFrom))
        {
            throw new LeaveInvalidInputException(
                "The employee employment profile values are invalid.");
        }

        var employee = await userRepository.GetByIdAsync(input.EmployeeUserId);
        if (employee is not { IsActive: true } ||
            employee.Role != UserRoles.Employee)
        {
            throw new LeaveNotFoundException("Active employee");
        }

        await leaveRepository.CreateEmploymentProfileAsync(
            new EmployeeEmploymentProfile(
                input.EmployeeUserId,
                input.DesignationId,
                input.EmploymentType,
                input.HireDate,
                input.EffectiveFrom,
                input.EffectiveTo));
    }

    private async Task RequireAdminAsync(int adminUserId)
    {
        var admin = adminUserId > 0
            ? await userRepository.GetByIdAsync(adminUserId)
            : null;
        if (admin is not { IsActive: true } ||
            admin.Role != UserRoles.Admin)
        {
            throw new LeaveUnauthorizedException();
        }
    }
}
