namespace FlowDesk.Domain.Enums;

public enum LeaveLedgerEntryType
{
    Accrual = 1,
    CarryForward = 2,
    Adjustment = 3,
    Reservation = 4,
    ReservationRelease = 5,
    ScheduledCommitment = 6,
    ScheduledRelease = 7,
    Used = 8,
    ScheduledToUsed = 9,
    Expiry = 10,
    Reversal = 11
}
