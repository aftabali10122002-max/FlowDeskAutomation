using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.Services;

public sealed record LeaveAccrualGrant(
    decimal Units,
    DateOnly PostingDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int PolicyId,
    int PolicyVersion,
    long? PayPeriodId,
    string IdempotencyKey);

public static class LeaveAccrualCalculator
{
    public static DateOnly DetermineFirstEligibleDate(
        DateOnly? hireDate,
        DateOnly policyEffectiveFrom,
        int? probationPeriodDays,
        bool allowsLeaveDuringProbation)
    {
        if (probationPeriodDays.HasValue && !hireDate.HasValue)
        {
            throw new InvalidOperationException(
                "A hire date is required to apply the policy probation period.");
        }

        var eligibleDate = hireDate.HasValue &&
            hireDate.Value > policyEffectiveFrom
            ? hireDate.Value
            : policyEffectiveFrom;

        if (!allowsLeaveDuringProbation &&
            probationPeriodDays is int probationDays &&
            hireDate is DateOnly startDate)
        {
            var afterProbation = startDate.AddDays(probationDays);
            if (afterProbation > eligibleDate)
            {
                eligibleDate = afterProbation;
            }
        }

        return eligibleDate;
    }

    public static decimal CalculateAnnualGrant(
        decimal annualAllowance,
        DateOnly entitlementPeriodStart,
        DateOnly entitlementPeriodEnd,
        DateOnly eligibleFrom,
        DateOnly? eligibleTo = null)
    {
        ValidateAllowance(annualAllowance);
        ValidateRange(entitlementPeriodStart, entitlementPeriodEnd);
        if (eligibleTo < eligibleFrom)
        {
            throw new ArgumentException(
                "The eligibility end date cannot precede the start date.");
        }

        var start = eligibleFrom > entitlementPeriodStart
            ? eligibleFrom
            : entitlementPeriodStart;
        var end = eligibleTo.HasValue &&
            eligibleTo.Value < entitlementPeriodEnd
            ? eligibleTo.Value
            : entitlementPeriodEnd;
        if (end < start)
        {
            return 0m;
        }

        var eligibleDays = end.DayNumber - start.DayNumber + 1;
        var periodDays = entitlementPeriodEnd.DayNumber -
            entitlementPeriodStart.DayNumber + 1;
        return RoundUnits(annualAllowance * eligibleDays / periodDays);
    }

    public static decimal CalculateMonthlyGrant(
        decimal annualAllowance,
        DateOnly monthStart,
        DateOnly eligibleFrom,
        DateOnly? eligibleTo = null)
    {
        ValidateAllowance(annualAllowance);
        var normalizedMonthStart = new DateOnly(
            monthStart.Year,
            monthStart.Month,
            1);
        var monthEnd = normalizedMonthStart.AddMonths(1).AddDays(-1);
        if (eligibleTo < eligibleFrom)
        {
            throw new ArgumentException(
                "The eligibility end date cannot precede the start date.");
        }

        var start = eligibleFrom > normalizedMonthStart
            ? eligibleFrom
            : normalizedMonthStart;
        var end = eligibleTo.HasValue && eligibleTo.Value < monthEnd
            ? eligibleTo.Value
            : monthEnd;
        if (end < start)
        {
            return 0m;
        }

        var eligibleDays = end.DayNumber - start.DayNumber + 1;
        var monthDays = monthEnd.DayNumber -
            normalizedMonthStart.DayNumber + 1;
        return RoundUnits(
            annualAllowance / 12m * eligibleDays / monthDays);
    }

    public static decimal CalculatePayPeriodGrant(
        decimal annualAllowance,
        LeavePayPeriod period,
        int expectedPeriodsInEntitlementYear,
        DateOnly eligibleFrom,
        DateOnly? eligibleTo,
        DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(period);
        ValidateAllowance(annualAllowance);
        if (expectedPeriodsInEntitlementYear <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedPeriodsInEntitlementYear));
        }

        if (eligibleTo < eligibleFrom)
        {
            throw new ArgumentException(
                "The eligibility end date cannot precede the start date.");
        }

        if (period.EndDate > businessDate || period.PostingDate > businessDate)
        {
            return 0m;
        }

        var start = eligibleFrom > period.StartDate
            ? eligibleFrom
            : period.StartDate;
        var end = eligibleTo.HasValue && eligibleTo.Value < period.EndDate
            ? eligibleTo.Value
            : period.EndDate;
        if (end < start)
        {
            return 0m;
        }

        var eligibleDays = end.DayNumber - start.DayNumber + 1;
        var totalPeriodDays = period.EndDate.DayNumber -
            period.StartDate.DayNumber + 1;
        var fullPeriodGrant = annualAllowance /
            expectedPeriodsInEntitlementYear;
        return RoundUnits(fullPeriodGrant * eligibleDays / totalPeriodDays);
    }

    public static decimal CalculateCumulativeDelta(
        decimal unroundedCumulativeEntitlement,
        decimal previouslyCreditedUnits)
    {
        if (unroundedCumulativeEntitlement < 0 ||
            previouslyCreditedUnits < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unroundedCumulativeEntitlement));
        }

        var roundedTarget = RoundUnits(unroundedCumulativeEntitlement);
        var delta = roundedTarget - previouslyCreditedUnits;
        if (delta < 0)
        {
            throw new InvalidOperationException(
                "Cumulative accrual cannot reverse previously credited units.");
        }

        return delta;
    }

    public static DateOnly GetAnniversaryDate(
        DateOnly anniversaryAnchor,
        int targetYear)
    {
        if (targetYear < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(targetYear));
        }

        var lastDay = DateTime.DaysInMonth(
            targetYear,
            anniversaryAnchor.Month);
        return new DateOnly(
            targetYear,
            anniversaryAnchor.Month,
            Math.Min(anniversaryAnchor.Day, lastDay));
    }

    public static DateOnly GetNextAnniversary(
        DateOnly anniversaryAnchor,
        DateOnly afterDate)
    {
        var candidate = GetAnniversaryDate(anniversaryAnchor, afterDate.Year);
        return candidate > afterDate
            ? candidate
            : GetAnniversaryDate(anniversaryAnchor, afterDate.Year + 1);
    }

    public static DateOnly GetBusinessDate(
        DateTime timestampUtc,
        TimeZoneInfo businessTimeZone)
    {
        ArgumentNullException.ThrowIfNull(businessTimeZone);
        if (timestampUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "The timestamp must be UTC.",
                nameof(timestampUtc));
        }

        return DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                timestampUtc,
                businessTimeZone));
    }

    public static string CreateIdempotencyKey(
        int employeeUserId,
        int leaveTypeId,
        int policyId,
        int policyVersion,
        DateOnly periodStart,
        DateOnly periodEnd,
        long? payPeriodId)
    {
        if (employeeUserId <= 0 ||
            leaveTypeId <= 0 ||
            policyId <= 0 ||
            policyVersion <= 0 ||
            periodEnd < periodStart ||
            (payPeriodId is <= 0))
        {
            throw new ArgumentException("The accrual key values are invalid.");
        }

        return string.Join(
            ':',
            "accrual",
            employeeUserId,
            leaveTypeId,
            policyId,
            policyVersion,
            periodStart.ToString("yyyyMMdd"),
            periodEnd.ToString("yyyyMMdd"),
            payPeriodId?.ToString() ?? "annual-monthly");
    }

    public static decimal RoundUnits(decimal units) =>
        decimal.Round(units, 2, MidpointRounding.AwayFromZero);

    private static void ValidateAllowance(decimal annualAllowance)
    {
        if (annualAllowance < 0 ||
            annualAllowance != RoundUnits(annualAllowance))
        {
            throw new ArgumentOutOfRangeException(nameof(annualAllowance));
        }
    }

    private static void ValidateRange(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            throw new ArgumentException(
                "The end date cannot precede the start date.");
        }
    }
}
