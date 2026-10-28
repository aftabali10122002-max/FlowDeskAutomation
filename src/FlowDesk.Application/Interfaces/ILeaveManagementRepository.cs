using FlowDesk.Application.DTOs;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public interface ILeaveManagementRepository
{
    Task<LeavePolicy?> GetPolicyAsync(int policyId, bool tracked = false);
    Task<IReadOnlyList<LeavePolicy>> GetPoliciesAsync();
    Task CreateDraftAsync(
        LeavePolicy policy,
        IReadOnlyList<LeavePolicyEntitlementInput> entitlements);
    Task UpdateDraftAsync(
        int policyId,
        LeavePolicy revisedPolicy,
        IReadOnlyList<LeavePolicyEntitlementInput> entitlements);
    Task<LeavePolicy> CreateVersionDraftAsync(
        int sourcePolicyId,
        DateOnly effectiveFrom);
    Task PublishPolicyAsync(int policyId, int adminUserId, DateTime publishedAtUtc);
    Task MarkSupersededAsync(
        int policyId,
        int successorPolicyId,
        DateTime supersededAtUtc);
    Task CreateAssignmentAsync(LeavePolicyAssignment assignment);
    Task CreateEmploymentProfileAsync(EmployeeEmploymentProfile profile);
    Task<IReadOnlyList<EmployeeLeaveMetadata>> GetEmployeeMetadataAsync(
        int employeeUserId,
        DateOnly asOfDate);
    Task<LeavePolicyResolutionData?> GetPolicyResolutionDataAsync(
        int employeeUserId,
        DateOnly rangeStart,
        DateOnly rangeEnd);
    Task<int?> GetEnabledLeaveTypeIdAsync(string codeOrName);
}
