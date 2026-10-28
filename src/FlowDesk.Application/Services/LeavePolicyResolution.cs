using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Application.Errors;

namespace FlowDesk.Application.Services;

public sealed class LeavePolicyConfigurationException(string message)
    : LeaveConfigurationException(message);

public enum LeavePolicyResolutionTier
{
    EmployeeAssignment = 1,
    Designation = 2,
    EmploymentType = 3,
    Department = 4,
    Company = 5
}

public sealed record LeavePolicyResolution(
    LeavePolicy Policy,
    LeavePolicyEntitlement Entitlement,
    LeavePolicyResolutionTier ResolvedScope);

public static class LeavePolicyResolver
{
    public static LeavePolicyResolution Resolve(
        User employee,
        IEnumerable<EmployeeEmploymentProfile> employmentProfiles,
        IEnumerable<LeavePolicyAssignment> assignments,
        IEnumerable<LeavePolicy> policies,
        int leaveTypeId,
        DateOnly requestedDate)
    {
        ArgumentNullException.ThrowIfNull(employee);
        ArgumentNullException.ThrowIfNull(employmentProfiles);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(policies);
        if (!employee.IsActive || leaveTypeId <= 0)
        {
            throw new LeavePolicyConfigurationException(
                "An active employee and valid leave type are required to resolve a leave policy.");
        }

        var profiles = employmentProfiles
            .Where(profile =>
                profile.UserId == employee.Id &&
                IsEffective(profile.EffectiveFrom, profile.EffectiveTo, requestedDate))
            .ToList();
        if (profiles.Count > 1)
        {
            throw new LeavePolicyConfigurationException(
                "Employee employment metadata has overlapping effective periods.");
        }

        var profile = profiles.SingleOrDefault();
        var assignmentMatches = assignments
            .Where(assignment =>
                assignment.UserId == employee.Id &&
                IsEffective(
                    assignment.EffectiveFrom,
                    assignment.EffectiveTo,
                    requestedDate))
            .ToList();
        if (assignmentMatches.Count > 1)
        {
            throw new LeavePolicyConfigurationException(
                "Employee policy assignments have overlapping effective periods.");
        }

        var availablePolicies = policies
            .Where(policy =>
                (policy.Status is LeavePolicyStatus.Published or
                    LeavePolicyStatus.Superseded) &&
                IsEffective(
                    policy.EffectiveFrom,
                    policy.EffectiveTo,
                    requestedDate) &&
                (policy.Status != LeavePolicyStatus.Superseded ||
                 !policies.Any(successor =>
                     successor.Id == policy.SupersededByPolicyId &&
                     successor.EffectiveFrom <= requestedDate)))
            .ToList();

        if (assignmentMatches.Count == 1)
        {
            var assignedPolicy = availablePolicies.SingleOrDefault(
                policy => policy.Id == assignmentMatches[0].LeavePolicyId);
            if (assignedPolicy is null)
            {
                throw new LeavePolicyConfigurationException(
                    "The employee's explicit policy assignment is not published or effective on the requested date.");
            }

            return ResolveEntitlement(
                assignedPolicy,
                leaveTypeId,
                LeavePolicyResolutionTier.EmployeeAssignment);
        }

        var designationMatch = profile?.DesignationId is int designationId
            ? FindPolicy(
                availablePolicies,
                LeavePolicyScope.Designation,
                policy => policy.DesignationId == designationId)
            : null;
        if (designationMatch is not null)
        {
            return ResolveEntitlement(
                designationMatch,
                leaveTypeId,
                LeavePolicyResolutionTier.Designation);
        }

        var employmentMatch = profile?.EmploymentType is EmploymentType type
            ? FindPolicy(
                availablePolicies,
                LeavePolicyScope.EmploymentType,
                policy => policy.EmploymentType == type)
            : null;
        if (employmentMatch is not null)
        {
            return ResolveEntitlement(
                employmentMatch,
                leaveTypeId,
                LeavePolicyResolutionTier.EmploymentType);
        }

        var departmentMatch = employee.DepartmentId is int departmentId
            ? FindPolicy(
                availablePolicies,
                LeavePolicyScope.Department,
                policy => policy.DepartmentId == departmentId)
            : null;
        if (departmentMatch is not null)
        {
            return ResolveEntitlement(
                departmentMatch,
                leaveTypeId,
                LeavePolicyResolutionTier.Department);
        }

        var companyMatch = FindPolicy(
            availablePolicies,
            LeavePolicyScope.Company,
            _ => true);
        if (companyMatch is null)
        {
            throw new LeavePolicyConfigurationException(
                "No published leave policy applies to the employee and requested date.");
        }

        return ResolveEntitlement(
            companyMatch,
            leaveTypeId,
            LeavePolicyResolutionTier.Company);
    }

    private static LeavePolicy? FindPolicy(
        IReadOnlyCollection<LeavePolicy> policies,
        LeavePolicyScope scope,
        Func<LeavePolicy, bool> targetMatches)
    {
        var matches = policies
            .Where(policy => policy.Scope == scope && targetMatches(policy))
            .OrderBy(policy => policy.EffectiveFrom)
            .ThenBy(policy => policy.Version)
            .ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count > 1)
        {
            throw new LeavePolicyConfigurationException(
                $"Multiple effective {scope} policies match the employee.");
        }

        return matches[0];
    }

    private static LeavePolicyResolution ResolveEntitlement(
        LeavePolicy policy,
        int leaveTypeId,
        LeavePolicyResolutionTier resolvedScope)
    {
        var entitlements = policy.Entitlements
            .Where(entitlement => entitlement.LeaveTypeId == leaveTypeId)
            .ToList();
        if (entitlements.Count != 1)
        {
            throw new LeavePolicyConfigurationException(
                $"The highest-precedence {resolvedScope} policy is missing exactly one entitlement for the requested leave type.");
        }

        return new LeavePolicyResolution(
            policy,
            entitlements[0],
            resolvedScope);
    }

    private static bool IsEffective(
        DateOnly from,
        DateOnly? to,
        DateOnly date) =>
        from <= date && (!to.HasValue || to.Value >= date);
}
