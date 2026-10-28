using FlowDesk.Application.DTOs;

namespace FlowDesk.Application.Services;

public sealed record LeaveBalanceSource(
    long SourceId,
    LeaveBalanceSourceKind Kind,
    decimal GrantedUnits,
    decimal ConsumedUnits,
    decimal ReservedUnits,
    DateOnly EarnedOn,
    DateOnly? ExpiresOn);

public sealed record RequestedLeaveDate(
    DateOnly Date,
    decimal Units,
    bool AllowsNegativeBalance = false);

public sealed record LeaveSourceAllocation(
    DateOnly LeaveDate,
    long SourceId,
    decimal Units);

public sealed record LeaveAllocationPlan(
    IReadOnlyList<LeaveSourceAllocation> Allocations,
    decimal UnfundedUnits);

public static class LeaveAllocationPlanner
{
    public static LeaveAllocationPlan PlanReservation(
        IEnumerable<RequestedLeaveDate> requestedDates,
        IEnumerable<LeaveBalanceSource> sources,
        DateOnly businessDate,
        bool allowsNegativeBalance)
    {
        ArgumentNullException.ThrowIfNull(requestedDates);
        return PlanReservation(
            requestedDates.Select(request => request with
            {
                AllowsNegativeBalance =
                    request.AllowsNegativeBalance || allowsNegativeBalance
            }),
            sources,
            businessDate);
    }

    public static LeaveAllocationPlan PlanReservation(
        IEnumerable<RequestedLeaveDate> requestedDates,
        IEnumerable<LeaveBalanceSource> sources,
        DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(requestedDates);
        ArgumentNullException.ThrowIfNull(sources);
        var leaveDates = requestedDates
            .OrderBy(x => x.Date)
            .ToList();
        if (leaveDates.Any(x => x.Units <= 0 ||
            x.Units != LeaveAccrualCalculator.RoundUnits(x.Units)))
        {
            throw new ArgumentException(
                "Requested leave units must be positive two-decimal values.",
                nameof(requestedDates));
        }

        var sourceList = sources.ToList();
        if (sourceList.Any(x =>
                x.SourceId <= 0 ||
                x.GrantedUnits < 0 ||
                x.ConsumedUnits < 0 ||
                x.ReservedUnits < 0 ||
                x.ConsumedUnits + x.ReservedUnits > x.GrantedUnits))
        {
            throw new ArgumentException(
                "A leave source has invalid or over-allocated units.",
                nameof(sources));
        }

        var remainingBySource = sourceList.ToDictionary(
            source => source.SourceId,
            source => source.GrantedUnits -
                source.ConsumedUnits -
                source.ReservedUnits);
        var allocations = new List<LeaveSourceAllocation>();
        var unfunded = 0m;

        foreach (var leaveDate in leaveDates)
        {
            var remainingForDate = leaveDate.Units;
            var eligibleSources = sourceList
                .Where(source =>
                    source.EarnedOn <= businessDate &&
                    source.EarnedOn <= leaveDate.Date &&
                    (!source.ExpiresOn.HasValue ||
                     source.ExpiresOn.Value >= leaveDate.Date) &&
                    remainingBySource[source.SourceId] > 0)
                .OrderBy(source =>
                    source.ExpiresOn ?? DateOnly.MaxValue)
                .ThenBy(source => source.EarnedOn)
                .ThenBy(source => source.SourceId);

            foreach (var source in eligibleSources)
            {
                var available = remainingBySource[source.SourceId];
                var allocated = Math.Min(available, remainingForDate);
                if (allocated <= 0)
                {
                    continue;
                }

                allocations.Add(new LeaveSourceAllocation(
                    leaveDate.Date,
                    source.SourceId,
                    allocated));
                remainingBySource[source.SourceId] -= allocated;
                remainingForDate -= allocated;
                if (remainingForDate == 0)
                {
                    break;
                }
            }

            if (remainingForDate > 0 && !leaveDate.AllowsNegativeBalance)
            {
                throw new InvalidOperationException(
                    "The employee has insufficient eligible leave balance.");
            }

            unfunded += remainingForDate;
        }

        return new LeaveAllocationPlan(allocations, unfunded);
    }

    public static DateOnly CalculateExpiryDate(
        DateOnly cycleEndDate,
        int expiryMonths)
    {
        if (expiryMonths <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expiryMonths));
        }

        var targetMonth = cycleEndDate.AddMonths(expiryMonths);
        var day = Math.Min(
            cycleEndDate.Day,
            DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month));
        return new DateOnly(targetMonth.Year, targetMonth.Month, day);
    }

    public static DateOnly GetBusinessDate(
        DateTime timestampUtc,
        TimeZoneInfo timeZone)
    {
        return LeaveAccrualCalculator.GetBusinessDate(
            timestampUtc,
            timeZone);
    }
}
