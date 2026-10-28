namespace FlowDesk.Domain.Entities;

public sealed class LeavePolicyAssignment
{
    public long Id { get; private set; }
    public int UserId { get; private set; }
    public User User { get; private set; } = null!;
    public int LeavePolicyId { get; private set; }
    public LeavePolicy LeavePolicy { get; private set; } = null!;
    public int AssignedByAdminUserId { get; private set; }
    public User AssignedByAdminUser { get; private set; } = null!;
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }

    private LeavePolicyAssignment() { }

    public LeavePolicyAssignment(
        int userId,
        int leavePolicyId,
        int assignedByAdminUserId,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo = null)
    {
        if (userId <= 0 ||
            leavePolicyId <= 0 ||
            assignedByAdminUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        if (effectiveTo < effectiveFrom)
        {
            throw new ArgumentException(
                "The effective end date cannot precede the start date.",
                nameof(effectiveTo));
        }

        UserId = userId;
        LeavePolicyId = leavePolicyId;
        AssignedByAdminUserId = assignedByAdminUserId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }
}
