using FlowDesk.Application.DTOs;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public interface ILeaveAccountingRepository
{
    Task<IReadOnlyList<LeaveType>> GetLeaveTypesAsync(bool enabledOnly);
    Task<IReadOnlyList<LeaveType>> GetEnabledLeaveTypesAsync();
    Task<LeaveType> CreateLeaveTypeAsync(LeaveType leaveType);
    Task<LeaveType> UpdateLeaveTypeAsync(
        int leaveTypeId,
        string displayName,
        bool isPaid,
        bool isEnabled);
    Task<IReadOnlyList<LeaveBalanceSummary>> GetBalancesAsync(
        int employeeUserId,
        DateOnly asOfDate);
    Task<IReadOnlyDictionary<int, IReadOnlyList<LeaveBalanceSummary>>>
        GetBalancesForEmployeesAsync(
            IReadOnlyList<int> employeeUserIds,
            DateOnly asOfDate);
    Task<IReadOnlyList<LeaveLedgerHistoryItem>> GetLedgerHistoryAsync(
        int employeeUserId,
        int? leaveTypeId,
        DateOnly? throughDate);
    Task<IReadOnlyList<LeaveBalanceSourceView>> GetAvailableSourcesAsync(
        int employeeUserId,
        int leaveTypeId,
        DateOnly businessDate);
    Task<bool> TryAppendLedgerWriteAsync(LeaveLedgerWrite write);
    Task<int> ProcessDueScheduledCommitmentsAsync(DateTime nowUtc);
    Task<int> ProcessDueAccrualsAsync(
        DateTime nowUtc,
        string businessTimeZoneId);
    Task<int> ProcessDueCarryForwardAndExpiryAsync(
        DateTime nowUtc,
        string businessTimeZoneId);
}
