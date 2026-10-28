namespace FlowDesk.Domain.Entities;

public sealed class LeavePolicyEntitlement
{
    public int Id { get; private set; }
    public int LeavePolicyId { get; private set; }
    public LeavePolicy LeavePolicy { get; private set; } = null!;
    public int LeaveTypeId { get; private set; }
    public LeaveType LeaveType { get; private set; } = null!;
    public decimal AnnualEntitlement { get; private set; }

    private LeavePolicyEntitlement() { }

    public LeavePolicyEntitlement(
        int leavePolicyId,
        int leaveTypeId,
        decimal annualEntitlement)
    {
        if (leavePolicyId <= 0 || leaveTypeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leavePolicyId));
        }

        if (annualEntitlement < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(annualEntitlement));
        }

        LeavePolicyId = leavePolicyId;
        LeaveTypeId = leaveTypeId;
        AnnualEntitlement = annualEntitlement;
    }
}
