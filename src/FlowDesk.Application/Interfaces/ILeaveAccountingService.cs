using FlowDesk.Application.DTOs;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public interface ILeaveAccountingService
{
    Task<IReadOnlyList<LeaveType>> GetEnabledLeaveTypesAsync();
    Task<IReadOnlyList<LeaveBalanceSummary>> GetMyBalancesAsync(
        int employeeUserId,
        DateOnly asOfDate);
    Task<IReadOnlyList<LeaveBalanceSummary>> GetBalancesForAdminAsync(
        int adminUserId,
        int employeeUserId,
        DateOnly asOfDate);
    Task<IReadOnlyList<LeaveLedgerHistoryItem>> GetMyHistoryAsync(
        int employeeUserId,
        int? leaveTypeId,
        DateOnly? throughDate);
    Task AdjustBalanceAsync(
        int adminUserId,
        int employeeUserId,
        int leaveTypeId,
        decimal units,
        string reason,
        string idempotencyKey,
        DateOnly effectiveDate);
    Task ReserveAsync(LeaveReservationRequest request);
}
