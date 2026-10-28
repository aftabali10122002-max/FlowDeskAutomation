namespace FlowDesk.Domain.Entities;

public sealed class LeaveRequestPolicyDay
{
    public int RequestId { get; private set; }
    public ApprovalRequest Request { get; private set; } = null!;
    public DateOnly LeaveDate { get; private set; }
    public int LeavePolicyId { get; private set; }
    public LeavePolicy LeavePolicy { get; private set; } = null!;
    public decimal Units { get; private set; }
    public bool AllowsNegativeBalance { get; private set; }

    private LeaveRequestPolicyDay() { }

    public LeaveRequestPolicyDay(
        int requestId,
        DateOnly leaveDate,
        int leavePolicyId,
        decimal units,
        bool allowsNegativeBalance)
    {
        if (requestId <= 0 || leavePolicyId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        if (units <= 0 ||
            units != decimal.Round(units, 2, MidpointRounding.AwayFromZero))
        {
            throw new ArgumentOutOfRangeException(nameof(units));
        }

        RequestId = requestId;
        LeaveDate = leaveDate;
        LeavePolicyId = leavePolicyId;
        Units = units;
        AllowsNegativeBalance = allowsNegativeBalance;
    }
}
