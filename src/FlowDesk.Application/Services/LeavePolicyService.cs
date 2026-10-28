using FlowDesk.Application.DTOs;
using FlowDesk.Application.Errors;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Services;

public sealed class LeavePolicyService(
    ILeaveManagementRepository leaveRepository,
    IUserRepository userRepository) : ILeavePolicyService
{
    public async Task<IReadOnlyList<LeavePolicy>> GetPoliciesAsync(
        int adminUserId)
    {
        await RequireAdminAsync(adminUserId);
        return await leaveRepository.GetPoliciesAsync();
    }

    public async Task<LeavePolicy> CreateDraftAsync(
        int adminUserId,
        LeavePolicyDraftInput input)
    {
        await RequireAdminAsync(adminUserId);
        ValidateEntitlementInput(input.Entitlements);
        var policy = CreatePolicy(input);
        await leaveRepository.CreateDraftAsync(policy, input.Entitlements);
        return await leaveRepository.GetPolicyAsync(policy.Id)
            ?? throw new LeaveNotFoundException("Leave policy");
    }

    public async Task<LeavePolicy> UpdateDraftAsync(
        int adminUserId,
        int policyId,
        LeavePolicyDraftInput input)
    {
        await RequireAdminAsync(adminUserId);
        ValidatePolicyId(policyId);
        ValidateEntitlementInput(input.Entitlements);
        var current = await leaveRepository.GetPolicyAsync(
            policyId,
            tracked: true)
            ?? throw new LeaveNotFoundException("Leave policy");
        if (current.Status != Domain.Enums.LeavePolicyStatus.Draft)
        {
            throw new LeaveConfigurationException(
                "Published policy versions are immutable; create a new version.");
        }

        if (input.Version != current.Version ||
            !string.Equals(
                input.Code.Trim(),
                current.Code,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new LeaveInvalidInputException(
                "A draft update cannot change its policy code or version.");
        }

        await leaveRepository.UpdateDraftAsync(
            policyId,
            CreatePolicy(input),
            input.Entitlements);
        return await leaveRepository.GetPolicyAsync(policyId)
            ?? throw new LeaveNotFoundException("Leave policy");
    }

    public async Task<LeavePolicy> CreateVersionDraftAsync(
        int adminUserId,
        int sourcePolicyId,
        DateOnly effectiveFrom)
    {
        await RequireAdminAsync(adminUserId);
        ValidatePolicyId(sourcePolicyId);
        return await leaveRepository.CreateVersionDraftAsync(
            sourcePolicyId,
            effectiveFrom);
    }

    public async Task PublishAsync(int adminUserId, int policyId)
    {
        await RequireAdminAsync(adminUserId);
        ValidatePolicyId(policyId);
        await leaveRepository.PublishPolicyAsync(
            policyId,
            adminUserId,
            DateTime.UtcNow);
    }

    public async Task MarkSupersededAsync(
        int adminUserId,
        int policyId,
        int successorPolicyId)
    {
        await RequireAdminAsync(adminUserId);
        ValidatePolicyId(policyId);
        ValidatePolicyId(successorPolicyId);
        await leaveRepository.MarkSupersededAsync(
            policyId,
            successorPolicyId,
            DateTime.UtcNow);
    }

    public async Task<LeavePolicyResolution> ResolveAsync(
        int employeeUserId,
        int leaveTypeId,
        DateOnly requestedDate)
    {
        if (employeeUserId <= 0 || leaveTypeId <= 0)
        {
            throw new LeaveInvalidInputException(
                "An employee and leave type are required.");
        }

        var data = await leaveRepository.GetPolicyResolutionDataAsync(
            employeeUserId,
            requestedDate,
            requestedDate)
            ?? throw new LeaveNotFoundException("Employee");
        return LeavePolicyResolver.Resolve(
            data.Employee,
            data.EmploymentProfiles,
            data.Assignments,
            data.Policies,
            leaveTypeId,
            requestedDate);
    }

    public async Task AssignPolicyAsync(
        int adminUserId,
        int employeeUserId,
        int policyId,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo)
    {
        await RequireAdminAsync(adminUserId);
        if (employeeUserId <= 0 || policyId <= 0)
        {
            throw new LeaveInvalidInputException(
                "An employee and leave policy are required.");
        }

        var user = await userRepository.GetByIdAsync(employeeUserId);
        if (user is not { IsActive: true } ||
            user.Role != UserRoles.Employee)
        {
            throw new LeaveNotFoundException("Active employee");
        }

        await leaveRepository.CreateAssignmentAsync(
            new LeavePolicyAssignment(
                employeeUserId,
                policyId,
                adminUserId,
                effectiveFrom,
                effectiveTo));
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

    private static LeavePolicy CreatePolicy(LeavePolicyDraftInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            return new LeavePolicy(
                input.Code,
                input.Name,
                input.Version,
                input.Scope,
                input.DepartmentId,
                input.DesignationId,
                input.EmploymentType,
                input.EffectiveFrom,
                input.EffectiveTo,
                input.DayCalculationMethod,
                input.LeaveWorkScheduleId,
                input.LeaveHolidayCalendarId,
                input.AllowsHalfDays,
                input.HalfDayUnits,
                input.IsAccrualEnabled,
                input.AccrualFrequency,
                input.AllowsCarryForward,
                input.CarryForwardCap,
                input.CarryForwardExpiryMonths,
                input.ProbationPeriodDays,
                input.AllowsLeaveDuringProbation,
                input.AllowsNegativeBalance,
                input.LeavePayrollCalendarId);
        }
        catch (ArgumentException exception)
        {
            throw new LeaveInvalidInputException(exception.Message);
        }
    }

    private static void ValidateEntitlementInput(
        IReadOnlyList<LeavePolicyEntitlementInput> entitlements)
    {
        ArgumentNullException.ThrowIfNull(entitlements);
        if (entitlements.Any(x =>
                x.LeaveTypeId <= 0 ||
                x.AnnualEntitlement < 0 ||
                x.AnnualEntitlement > 9_999_999.99m ||
                x.AnnualEntitlement !=
                    LeaveAccrualCalculator.RoundUnits(x.AnnualEntitlement)))
        {
            throw new LeaveInvalidInputException(
                "Entitlements must reference a leave type and contain non-negative values with at most two decimal places.");
        }

        if (entitlements.Select(x => x.LeaveTypeId).Distinct().Count() !=
            entitlements.Count)
        {
            throw new LeaveInvalidInputException(
                "A leave type can have only one entitlement per policy.");
        }
    }

    private static void ValidatePolicyId(int policyId)
    {
        if (policyId <= 0)
        {
            throw new LeaveInvalidInputException("A valid leave policy is required.");
        }
    }
}
