using FlowDesk.Application.Services;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class LeaveDayCalculatorTests
{
    [Fact]
    public void WorkingDayCalculationSkipsWeekendAndConfiguredHoliday()
    {
        var policy = CreatePolicy(
            LeaveDayCalculationMethod.WorkingDays,
            scheduleId: 10,
            holidayCalendarId: 20,
            allowsHalfDays: true,
            halfDayUnits: 0.5m);
        var entitlement = new LeavePolicyEntitlement(1, 1, 20m);
        var charges = LeaveDayCalculator.Calculate(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 8),
            LeaveDayPortion.FullDay,
            LeaveDayPortion.FullDay,
            _ => new LeavePolicyResolution(
                policy,
                entitlement,
                LeavePolicyResolutionTier.Company),
            new Dictionary<int, IReadOnlySet<DayOfWeek>>
            {
                [10] = new HashSet<DayOfWeek>
                {
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday
                }
            },
            new Dictionary<int, IReadOnlySet<DateOnly>>
            {
                [20] = new HashSet<DateOnly>
                {
                    new(2026, 11, 4)
                }
            });

        Assert.Equal(
            new[] { 1m, 1m, 1m, 1m },
            charges.Where(charge => charge.Portion == LeaveDayPortion.FullDay)
                .Select(charge => charge.Units));
        Assert.Equal(4m, charges.Sum(charge => charge.Units));
        Assert.DoesNotContain(
            charges,
            charge => charge.Date == new DateOnly(2026, 11, 4));
    }

    [Fact]
    public void SingleDayHalfDayCanBeSpecifiedAsTheEndPortion()
    {
        var policy = CreatePolicy(
            LeaveDayCalculationMethod.CalendarDays,
            allowsHalfDays: true,
            halfDayUnits: 0.5m);
        var charges = LeaveDayCalculator.Calculate(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 2),
            LeaveDayPortion.FullDay,
            LeaveDayPortion.HalfDay,
            _ => new LeavePolicyResolution(
                policy,
                new LeavePolicyEntitlement(1, 1, 20m),
                LeavePolicyResolutionTier.Company),
            new Dictionary<int, IReadOnlySet<DayOfWeek>>(),
            new Dictionary<int, IReadOnlySet<DateOnly>>());

        Assert.Single(charges);
        Assert.Equal(0.5m, charges[0].Units);
    }

    private static LeavePolicy CreatePolicy(
        LeaveDayCalculationMethod method,
        int? scheduleId = null,
        int? holidayCalendarId = null,
        bool allowsHalfDays = false,
        decimal? halfDayUnits = null)
    {
        return new LeavePolicy(
            "CALC-TEST",
            "Calculation test policy",
            1,
            LeavePolicyScope.Company,
            null,
            null,
            null,
            new DateOnly(2020, 1, 1),
            null,
            method,
            scheduleId,
            holidayCalendarId,
            allowsHalfDays,
            halfDayUnits,
            isAccrualEnabled: false,
            accrualFrequency: null,
            allowsCarryForward: false,
            carryForwardCap: null,
            carryForwardExpiryMonths: null,
            probationPeriodDays: null,
            allowsLeaveDuringProbation: true,
            allowsNegativeBalance: false);
    }
}
