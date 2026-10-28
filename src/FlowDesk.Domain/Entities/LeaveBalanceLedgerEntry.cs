using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public sealed class LeaveBalanceLedgerEntry
{
    public long Id { get; private set; }
    public int EmployeeUserId { get; private set; }
    public User Employee { get; private set; } = null!;
    public int LeaveTypeId { get; private set; }
    public LeaveType LeaveType { get; private set; } = null!;
    public int? LeavePolicyId { get; private set; }
    public LeavePolicy? LeavePolicy { get; private set; }
    public int? RequestId { get; private set; }
    public ApprovalRequest? Request { get; private set; }
    public long? PayPeriodId { get; private set; }
    public LeavePayPeriod? PayPeriod { get; private set; }
    public LeaveLedgerEntryType EntryType { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public DateOnly? LeaveDate { get; private set; }
    public decimal Units { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public int? ActorUserId { get; private set; }
    public User? Actor { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateOnly? ExpiresOn { get; private set; }
    public long? SourceEntryId { get; private set; }
    public LeaveBalanceLedgerEntry? SourceEntry { get; private set; }
    public ICollection<LeaveBalanceAllocation> Allocations { get; private set; }
        = [];

    private LeaveBalanceLedgerEntry() { }

    public LeaveBalanceLedgerEntry(
        int employeeUserId,
        int leaveTypeId,
        int? leavePolicyId,
        int? requestId,
        long? payPeriodId,
        LeaveLedgerEntryType entryType,
        DateOnly effectiveDate,
        decimal units,
        string idempotencyKey,
        DateTime createdAtUtc,
        string? reason = null,
        int? actorUserId = null,
        DateOnly? expiresOn = null,
        long? sourceEntryId = null,
        DateOnly? leaveDate = null)
    {
        if (employeeUserId <= 0 ||
            leaveTypeId <= 0 ||
            leavePolicyId is <= 0 ||
            requestId is <= 0 ||
            payPeriodId is <= 0 ||
            actorUserId is <= 0 ||
            sourceEntryId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(employeeUserId));
        }

        if (!Enum.IsDefined(entryType) ||
            units == 0 ||
            units != decimal.Round(units, 2, MidpointRounding.AwayFromZero))
        {
            throw new ArgumentException(
                "Ledger entries require a valid type and non-zero units with two decimal places.");
        }

        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Ledger timestamps must be UTC.",
                nameof(createdAtUtc));
        }

        if (units < 0 &&
            entryType != LeaveLedgerEntryType.Adjustment &&
            entryType != LeaveLedgerEntryType.Reversal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(units),
                "Only an audited adjustment or reversal may post negative units.");
        }

        if ((entryType is LeaveLedgerEntryType.Reservation or
                LeaveLedgerEntryType.ReservationRelease or
                LeaveLedgerEntryType.ScheduledCommitment or
                LeaveLedgerEntryType.ScheduledRelease or
                LeaveLedgerEntryType.Used or
                LeaveLedgerEntryType.ScheduledToUsed) &&
            !requestId.HasValue)
        {
            throw new ArgumentException(
                "Request-linked ledger entries require a request.",
                nameof(requestId));
        }

        if ((entryType is LeaveLedgerEntryType.ScheduledCommitment or
                LeaveLedgerEntryType.ScheduledRelease or
                LeaveLedgerEntryType.ScheduledToUsed) &&
            !leaveDate.HasValue)
        {
            throw new ArgumentException(
                "Scheduled ledger entries require the associated leave date.",
                nameof(leaveDate));
        }

        if ((entryType is LeaveLedgerEntryType.Adjustment or
                LeaveLedgerEntryType.Reversal) &&
            (actorUserId is null || string.IsNullOrWhiteSpace(reason)))
        {
            throw new ArgumentException(
                "Adjustments and reversals require an actor and reason.");
        }

        if (entryType == LeaveLedgerEntryType.Expiry &&
            (sourceEntryId is null || !expiresOn.HasValue))
        {
            throw new ArgumentException(
                "Expired balance entries must reference an expiring source.");
        }

        if (entryType == LeaveLedgerEntryType.Reversal &&
            sourceEntryId is null)
        {
            throw new ArgumentException(
                "A reversal must reference the ledger entry it reverses.",
                nameof(sourceEntryId));
        }

        IdempotencyKey = RequireText(
            idempotencyKey,
            200,
            nameof(idempotencyKey));
        if (reason?.Trim().Length > 1000)
        {
            throw new ArgumentException(
                "Reason cannot exceed 1000 characters.",
                nameof(reason));
        }

        EmployeeUserId = employeeUserId;
        LeaveTypeId = leaveTypeId;
        LeavePolicyId = leavePolicyId;
        RequestId = requestId;
        PayPeriodId = payPeriodId;
        EntryType = entryType;
        EffectiveDate = effectiveDate;
        LeaveDate = leaveDate;
        Units = units;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ActorUserId = actorUserId;
        ExpiresOn = expiresOn;
        SourceEntryId = sourceEntryId;
        CreatedAtUtc = createdAtUtc;
    }

    private static string RequireText(
        string value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Trim().Length > maximumLength)
        {
            throw new ArgumentException(
                $"A value is required and cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return value.Trim();
    }
}

public sealed class LeaveBalanceAllocation
{
    public long Id { get; private set; }
    public long LedgerEntryId { get; private set; }
    public LeaveBalanceLedgerEntry LedgerEntry { get; private set; } = null!;
    public long SourceEntryId { get; private set; }
    public LeaveBalanceLedgerEntry SourceEntry { get; private set; } = null!;
    public DateOnly LeaveDate { get; private set; }
    public decimal Units { get; private set; }

    private LeaveBalanceAllocation() { }

    public LeaveBalanceAllocation(
        long ledgerEntryId,
        long sourceEntryId,
        DateOnly leaveDate,
        decimal units)
    {
        if (ledgerEntryId <= 0 || sourceEntryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ledgerEntryId));
        }

        if (ledgerEntryId == sourceEntryId ||
            units <= 0 ||
            units != decimal.Round(units, 2, MidpointRounding.AwayFromZero))
        {
            throw new ArgumentException("The balance allocation is invalid.");
        }

        LedgerEntryId = ledgerEntryId;
        SourceEntryId = sourceEntryId;
        LeaveDate = leaveDate;
        Units = units;
    }
}
