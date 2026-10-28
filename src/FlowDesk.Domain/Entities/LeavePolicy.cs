using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public sealed class LeavePolicy
{
    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public LeavePolicyStatus Status { get; private set; } =
        LeavePolicyStatus.Draft;
    public DateTime? PublishedAtUtc { get; private set; }
    public int? PublishedByUserId { get; private set; }
    public User? PublishedByUser { get; private set; }
    public DateTime? SupersededAtUtc { get; private set; }
    public int? SupersededByPolicyId { get; private set; }
    public LeavePolicy? SupersededByPolicy { get; private set; }
    public LeavePolicyScope Scope { get; private set; }
    public int? DepartmentId { get; private set; }
    public Department? Department { get; private set; }
    public int? DesignationId { get; private set; }
    public Designation? Designation { get; private set; }
    public EmploymentType? EmploymentType { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public LeaveDayCalculationMethod DayCalculationMethod { get; private set; }
    public int? LeaveWorkScheduleId { get; private set; }
    public LeaveWorkSchedule? LeaveWorkSchedule { get; private set; }
    public int? LeaveHolidayCalendarId { get; private set; }
    public LeaveHolidayCalendar? LeaveHolidayCalendar { get; private set; }
    public int? LeavePayrollCalendarId { get; private set; }
    public LeavePayrollCalendar? LeavePayrollCalendar { get; private set; }
    public bool AllowsHalfDays { get; private set; }
    public decimal? HalfDayUnits { get; private set; }
    public bool IsAccrualEnabled { get; private set; }
    public LeaveAccrualFrequency? AccrualFrequency { get; private set; }
    public bool AllowsCarryForward { get; private set; }
    public decimal? CarryForwardCap { get; private set; }
    public int? CarryForwardExpiryMonths { get; private set; }
    public int? ProbationPeriodDays { get; private set; }
    public bool AllowsLeaveDuringProbation { get; private set; }
    public bool AllowsNegativeBalance { get; private set; }
    private readonly List<LeavePolicyEntitlement> _entitlements = [];
    public IReadOnlyCollection<LeavePolicyEntitlement> Entitlements =>
        _entitlements.AsReadOnly();

    private LeavePolicy() { }

    public LeavePolicy(
        string code,
        string name,
        int version,
        LeavePolicyScope scope,
        int? departmentId,
        int? designationId,
        EmploymentType? employmentType,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        LeaveDayCalculationMethod dayCalculationMethod,
        int? leaveWorkScheduleId,
        int? leaveHolidayCalendarId,
        bool allowsHalfDays,
        decimal? halfDayUnits,
        bool isAccrualEnabled,
        LeaveAccrualFrequency? accrualFrequency,
        bool allowsCarryForward,
        decimal? carryForwardCap,
        int? carryForwardExpiryMonths,
        int? probationPeriodDays,
        bool allowsLeaveDuringProbation,
        bool allowsNegativeBalance,
        int? leavePayrollCalendarId = null)
    {
        Code = RequireText(code, 50, nameof(code)).ToUpperInvariant();
        Name = RequireText(name, 120, nameof(name));
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        if (!Enum.IsDefined(scope) ||
            !Enum.IsDefined(dayCalculationMethod) ||
            (employmentType.HasValue &&
             !Enum.IsDefined(employmentType.Value)) ||
            (accrualFrequency.HasValue &&
             !Enum.IsDefined(accrualFrequency.Value)))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        if (!HasValidScopeTarget(
                scope,
                departmentId,
                designationId,
                employmentType))
        {
            throw new ArgumentException(
                "The policy scope must have exactly its matching target.");
        }

        if (departmentId is <= 0 ||
            designationId is <= 0 ||
            leaveWorkScheduleId is <= 0 ||
        leaveHolidayCalendarId is <= 0 ||
        leavePayrollCalendarId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(departmentId));
        }

        if (effectiveTo < effectiveFrom)
        {
            throw new ArgumentException(
                "The effective end date cannot precede the start date.",
                nameof(effectiveTo));
        }

        if ((dayCalculationMethod == LeaveDayCalculationMethod.WorkingDays &&
             !leaveWorkScheduleId.HasValue) ||
            (allowsHalfDays && halfDayUnits is not > 0 or >= 1) ||
            (!allowsHalfDays && halfDayUnits.HasValue))
        {
            throw new ArgumentException(
                "The leave calculation settings are inconsistent.");
        }

        if ((isAccrualEnabled && !accrualFrequency.HasValue) ||
            (!isAccrualEnabled && accrualFrequency.HasValue))
        {
            throw new ArgumentException(
                "An accrual frequency is required only when accrual is enabled.",
                nameof(accrualFrequency));
        }

        if ((accrualFrequency == LeaveAccrualFrequency.PerPayPeriod) !=
            leavePayrollCalendarId.HasValue)
        {
            throw new ArgumentException(
                "Per-pay-period accrual requires a payroll calendar and other accrual frequencies must not specify one.",
                nameof(leavePayrollCalendarId));
        }

        if ((allowsCarryForward &&
             (carryForwardCap is null or < 0 ||
              carryForwardExpiryMonths is <= 0)) ||
            (!allowsCarryForward &&
             (carryForwardCap.HasValue ||
              carryForwardExpiryMonths.HasValue)))
        {
            throw new ArgumentException(
                "Carry-forward settings are inconsistent.",
                nameof(carryForwardCap));
        }

        if (probationPeriodDays is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(probationPeriodDays));
        }

        Version = version;
        Scope = scope;
        DepartmentId = departmentId;
        DesignationId = designationId;
        EmploymentType = employmentType;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        DayCalculationMethod = dayCalculationMethod;
        LeaveWorkScheduleId = leaveWorkScheduleId;
        LeaveHolidayCalendarId = leaveHolidayCalendarId;
        AllowsHalfDays = allowsHalfDays;
        HalfDayUnits = halfDayUnits;
        IsAccrualEnabled = isAccrualEnabled;
        AccrualFrequency = accrualFrequency;
        AllowsCarryForward = allowsCarryForward;
        CarryForwardCap = carryForwardCap;
        CarryForwardExpiryMonths = carryForwardExpiryMonths;
        ProbationPeriodDays = probationPeriodDays;
        AllowsLeaveDuringProbation = allowsLeaveDuringProbation;
        AllowsNegativeBalance = allowsNegativeBalance;
        LeavePayrollCalendarId = leavePayrollCalendarId;
    }

    public void Publish(int publishedByUserId, DateTime publishedAtUtc)
    {
        if (Status != LeavePolicyStatus.Draft)
        {
            throw new InvalidOperationException(
                "Only a draft leave policy can be published.");
        }

        if (publishedByUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(publishedByUserId));
        }

        EnsureUtc(publishedAtUtc, nameof(publishedAtUtc));

        Status = LeavePolicyStatus.Published;
        PublishedAtUtc = publishedAtUtc;
        PublishedByUserId = publishedByUserId;
    }

    public void MarkSupersededBy(
        LeavePolicy successor,
        DateTime supersededAtUtc)
    {
        ArgumentNullException.ThrowIfNull(successor);
        EnsureUtc(supersededAtUtc, nameof(supersededAtUtc));

        if (Status != LeavePolicyStatus.Published ||
            successor.Status != LeavePolicyStatus.Published)
        {
            throw new InvalidOperationException(
                "Only a published policy can be superseded by a published policy.");
        }

        if (Id <= 0 ||
            successor.Id <= 0 ||
            Id == successor.Id ||
            Version == int.MaxValue ||
            successor.Version != Version + 1 ||
            !HasSamePolicyScope(successor) ||
            successor.EffectiveFrom <= EffectiveFrom ||
            DateOnly.FromDateTime(supersededAtUtc) < successor.EffectiveFrom)
        {
            throw new InvalidOperationException(
                "The successor must be the next published version for the same policy scope and have an effective date that has begun.");
        }

        Status = LeavePolicyStatus.Superseded;
        SupersededAtUtc = supersededAtUtc;
        SupersededByPolicyId = successor.Id;
        SupersededByPolicy = successor;
    }

    public void AddEntitlement(LeavePolicyEntitlement entitlement)
    {
        ArgumentNullException.ThrowIfNull(entitlement);
        EnsureDraft();

        if (_entitlements.Any(
                existing => existing.LeaveTypeId == entitlement.LeaveTypeId))
        {
            throw new InvalidOperationException(
                "A leave type can have only one entitlement in a policy.");
        }

        _entitlements.Add(entitlement);
    }

    public void ReplaceDraftConfiguration(LeavePolicy revised)
    {
        ArgumentNullException.ThrowIfNull(revised);
        EnsureDraft();
        if (revised.Status != LeavePolicyStatus.Draft ||
            (Id != 0 && revised.Id != 0 && Id != revised.Id))
        {
            throw new InvalidOperationException(
                "Only a draft policy can be updated from another draft.");
        }

        Code = revised.Code;
        Name = revised.Name;
        Scope = revised.Scope;
        DepartmentId = revised.DepartmentId;
        DesignationId = revised.DesignationId;
        EmploymentType = revised.EmploymentType;
        EffectiveFrom = revised.EffectiveFrom;
        EffectiveTo = revised.EffectiveTo;
        DayCalculationMethod = revised.DayCalculationMethod;
        LeaveWorkScheduleId = revised.LeaveWorkScheduleId;
        LeaveHolidayCalendarId = revised.LeaveHolidayCalendarId;
        LeavePayrollCalendarId = revised.LeavePayrollCalendarId;
        AllowsHalfDays = revised.AllowsHalfDays;
        HalfDayUnits = revised.HalfDayUnits;
        IsAccrualEnabled = revised.IsAccrualEnabled;
        AccrualFrequency = revised.AccrualFrequency;
        AllowsCarryForward = revised.AllowsCarryForward;
        CarryForwardCap = revised.CarryForwardCap;
        CarryForwardExpiryMonths = revised.CarryForwardExpiryMonths;
        ProbationPeriodDays = revised.ProbationPeriodDays;
        AllowsLeaveDuringProbation = revised.AllowsLeaveDuringProbation;
        AllowsNegativeBalance = revised.AllowsNegativeBalance;
    }

    public void RemoveEntitlement(int leaveTypeId)
    {
        EnsureDraft();
        var entitlement = _entitlements.SingleOrDefault(
            existing => existing.LeaveTypeId == leaveTypeId);
        if (entitlement is not null)
        {
            _entitlements.Remove(entitlement);
        }
    }

    public void ClearDraftEntitlements()
    {
        EnsureDraft();
        _entitlements.Clear();
    }

    public static bool HasValidScopeTarget(
        LeavePolicyScope scope,
        int? departmentId,
        int? designationId,
        EmploymentType? employmentType)
    {
        return scope switch
        {
            LeavePolicyScope.Company =>
                !departmentId.HasValue &&
                !designationId.HasValue &&
                !employmentType.HasValue,
            LeavePolicyScope.Department =>
                departmentId.HasValue &&
                !designationId.HasValue &&
                !employmentType.HasValue,
            LeavePolicyScope.Designation =>
                !departmentId.HasValue &&
                designationId.HasValue &&
                !employmentType.HasValue,
            LeavePolicyScope.EmploymentType =>
                !departmentId.HasValue &&
                !designationId.HasValue &&
                employmentType.HasValue,
            _ => false
        };
    }

    private bool HasSamePolicyScope(LeavePolicy other)
    {
        return string.Equals(Code, other.Code, StringComparison.Ordinal) &&
            Scope == other.Scope &&
            DepartmentId == other.DepartmentId &&
            DesignationId == other.DesignationId &&
            EmploymentType == other.EmploymentType;
    }

    private void EnsureDraft()
    {
        if (Status != LeavePolicyStatus.Draft)
        {
            throw new InvalidOperationException(
                "Published leave policy entitlements are immutable.");
        }
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "The timestamp must be UTC.",
                parameterName);
        }
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
