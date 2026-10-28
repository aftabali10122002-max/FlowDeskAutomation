using FlowDesk.Application.DTOs;
using FlowDesk.Application.Services;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public interface ILeavePolicyService
{
    Task<IReadOnlyList<LeavePolicy>> GetPoliciesAsync(int adminUserId);
    Task<LeavePolicy> CreateDraftAsync(
        int adminUserId,
        LeavePolicyDraftInput input);
    Task<LeavePolicy> UpdateDraftAsync(
        int adminUserId,
        int policyId,
        LeavePolicyDraftInput input);
    Task<LeavePolicy> CreateVersionDraftAsync(
        int adminUserId,
        int sourcePolicyId,
        DateOnly effectiveFrom);
    Task PublishAsync(int adminUserId, int policyId);
    Task MarkSupersededAsync(
        int adminUserId,
        int policyId,
        int successorPolicyId);
    Task<LeavePolicyResolution> ResolveAsync(
        int employeeUserId,
        int leaveTypeId,
        DateOnly requestedDate);
    Task AssignPolicyAsync(
        int adminUserId,
        int employeeUserId,
        int policyId,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo);
}
