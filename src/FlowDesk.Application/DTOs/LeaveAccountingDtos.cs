using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.DTOs;

public enum LeaveBalanceSourceKind
{
    CarryForward = 1,
    CurrentCycleEarned = 2
}

public sealed record LeaveBalanceSummary(
    int EmployeeUserId,
    int LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    decimal Earned,
    decimal Adjustments,
    decimal CarryForward,
    decimal Used,
    decimal Pending,
    decimal Scheduled,
    decimal Expired,
    decimal Available);

public sealed record LeaveLedgerAllocationInput(
    long SourceEntryId,
    DateOnly LeaveDate,
    decimal Units);

public sealed record LeaveLedgerWrite(
    LeaveBalanceLedgerEntry Entry,
    IReadOnlyList<LeaveLedgerAllocationInput> Allocations);

public sealed record LeaveDateRequestInput(
    DateOnly Date,
    decimal Units);

public sealed record LeaveReservationRequest(
    int EmployeeUserId,
    int LeaveTypeId,
    int RequestId,
    int PolicyId,
    DateTime CreatedAtUtc,
    IReadOnlyList<LeaveDateRequestInput> Dates,
    bool AllowsNegativeBalance);

public sealed record LeaveBalanceSourceView(
    long SourceId,
    LeaveBalanceSourceKind Kind,
    decimal GrantedUnits,
    decimal ConsumedUnits,
    decimal ReservedUnits,
    DateOnly EarnedOn,
    DateOnly? ExpiresOn);

public sealed record LeaveLedgerHistoryItem(
    long Id,
    int LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    LeaveLedgerEntryType EntryType,
    decimal Units,
    DateOnly EffectiveDate,
    DateTime CreatedAtUtc,
    DateOnly? ExpiresOn,
    int? PolicyId,
    int? RequestId,
    string? Reason,
    int? ActorUserId);

public sealed record LeaveTypeInput(
    string Code,
    string DisplayName,
    bool IsPaid);

public sealed record LeaveTypeUpdateInput(
    string DisplayName,
    bool IsPaid,
    bool IsEnabled);

public sealed record LeaveManualAdjustmentInput(
    int EmployeeUserId,
    int LeaveTypeId,
    decimal Units,
    DateOnly EffectiveDate,
    string Reason,
    string IdempotencyKey);

public sealed record LeaveCancellationInput(string Reason);

public sealed record LeaveTeamBalanceSummary(
    int EmployeeUserId,
    string EmployeeName,
    IReadOnlyList<LeaveBalanceSummary> Balances);
