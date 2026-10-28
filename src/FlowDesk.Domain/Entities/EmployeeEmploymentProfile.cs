using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public sealed class EmployeeEmploymentProfile
{
    public long Id { get; private set; }
    public int UserId { get; private set; }
    public User User { get; private set; } = null!;
    public int? DesignationId { get; private set; }
    public Designation? Designation { get; private set; }
    public EmploymentType? EmploymentType { get; private set; }
    public DateOnly? HireDate { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private EmployeeEmploymentProfile() { }

    public EmployeeEmploymentProfile(
        int userId,
        int? designationId,
        EmploymentType? employmentType,
        DateOnly? hireDate,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo = null)
    {
        if (userId <= 0 || designationId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        if (employmentType.HasValue &&
            !Enum.IsDefined(employmentType.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(employmentType));
        }

        if (effectiveTo < effectiveFrom)
        {
            throw new ArgumentException(
                "The effective end date cannot precede the start date.",
                nameof(effectiveTo));
        }

        UserId = userId;
        DesignationId = designationId;
        EmploymentType = employmentType;
        HireDate = hireDate;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }
}
