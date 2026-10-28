using FlowDesk.Application.Services;
using FlowDesk.Application.DTOs;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class LeaveAccountingCalculatorTests
{
    [Fact]
    public void AnnualAccrualCreditsFullEntitlementForFullCycle()
    {
        var amount = LeaveAccrualCalculator.CalculateAnnualGrant(
            24m,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            new DateOnly(2026, 1, 1));

        Assert.Equal(24m, amount);
    }

    [Fact]
    public void AnnualAccrualProratesRemainingCycle()
    {
        var amount = LeaveAccrualCalculator.CalculateAnnualGrant(
            24m,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            new DateOnly(2026, 7, 1));

        Assert.Equal(
            LeaveAccrualCalculator.RoundUnits(24m * 184m / 365m),
            amount);
    }

    [Fact]
    public void MonthlyAccrualAndPartialMonthUseCalendarDayProration()
    {
        Assert.Equal(
            2m,
            LeaveAccrualCalculator.CalculateMonthlyGrant(
                24m,
                new DateOnly(2026, 3, 1),
                new DateOnly(2026, 3, 1)));
        Assert.Equal(
            1m,
            LeaveAccrualCalculator.CalculateMonthlyGrant(
                24m,
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 4, 16)));
    }

    [Fact]
    public void PayPeriodAccrualRequiresCompletedPeriodAndPostingDate()
    {
        var period = new LeavePayPeriod(
            leavePayrollCalendarId: 1,
            startDate: new DateOnly(2026, 3, 1),
            endDate: new DateOnly(2026, 3, 14),
            postingDate: new DateOnly(2026, 3, 16),
            entitlementYear: 2026,
            periodNumber: 5);

        Assert.Equal(
            1m,
            LeaveAccrualCalculator.CalculatePayPeriodGrant(
                26m,
                period,
                expectedPeriodsInEntitlementYear: 26,
                eligibleFrom: new DateOnly(2026, 3, 1),
                eligibleTo: null,
                businessDate: new DateOnly(2026, 3, 16)));
        Assert.Equal(
            0m,
            LeaveAccrualCalculator.CalculatePayPeriodGrant(
                26m,
                period,
                expectedPeriodsInEntitlementYear: 26,
                eligibleFrom: new DateOnly(2026, 3, 1),
                eligibleTo: null,
                businessDate: new DateOnly(2026, 3, 15)));
        Assert.Equal(
            LeaveAccrualCalculator.RoundUnits(1m * 7m / 14m),
            LeaveAccrualCalculator.CalculatePayPeriodGrant(
                26m,
                period,
                expectedPeriodsInEntitlementYear: 26,
                eligibleFrom: new DateOnly(2026, 3, 8),
                eligibleTo: null,
                businessDate: new DateOnly(2026, 3, 16)));
    }

    [Fact]
    public void PerPayPeriodPolicyRequiresPayrollCalendar()
    {
        Assert.Throws<ArgumentException>(() => CreatePolicy(
            isAccrualEnabled: true,
            accrualFrequency: LeaveAccrualFrequency.PerPayPeriod));
    }

    [Fact]
    public void FebruaryTwentyNinthAnniversaryClampsToFebruaryTwentyEighth()
    {
        var leapAnniversary = new DateOnly(2024, 2, 29);

        Assert.Equal(
            new DateOnly(2025, 2, 28),
            LeaveAccrualCalculator.GetAnniversaryDate(leapAnniversary, 2025));
        Assert.Equal(
            new DateOnly(2028, 2, 29),
            LeaveAccrualCalculator.GetAnniversaryDate(leapAnniversary, 2028));
        Assert.Equal(
            new DateOnly(2026, 2, 28),
            LeaveAccrualCalculator.GetNextAnniversary(
                leapAnniversary,
                new DateOnly(2025, 3, 1)));
    }

    [Fact]
    public void CumulativeAccrualReconcilesRoundingWithoutDrift()
    {
        var alreadyCredited = 0m;
        var grants = new List<decimal>();
        for (var period = 1; period <= 12; period++)
        {
            var delta = LeaveAccrualCalculator.CalculateCumulativeDelta(
                10m * period / 12m,
                alreadyCredited);
            grants.Add(delta);
            alreadyCredited += delta;
        }

        Assert.Equal(10m, alreadyCredited);
        Assert.All(grants, grant => Assert.Equal(
            grant,
            LeaveAccrualCalculator.RoundUnits(grant)));
    }

    [Fact]
    public void AccrualIdempotencyKeyContainsEmployeeLeaveTypeAndPolicyVersion()
    {
        var key = LeaveAccrualCalculator.CreateIdempotencyKey(
            employeeUserId: 4,
            leaveTypeId: 2,
            policyId: 9,
            policyVersion: 3,
            periodStart: new DateOnly(2026, 1, 1),
            periodEnd: new DateOnly(2026, 1, 31),
            payPeriodId: 17);

        Assert.Equal(
            "accrual:4:2:9:3:20260101:20260131:17",
            key);
    }

    [Fact]
    public void CarryForwardExpiryUsesCalendarMonthEndAndInclusiveDate()
    {
        Assert.Equal(
            new DateOnly(2025, 2, 28),
            LeaveAllocationPlanner.CalculateExpiryDate(
                new DateOnly(2024, 12, 31),
                2));
    }

    [Fact]
    public void ReservationUsesEarliestExpiringCarryForwardThenCurrentCycle()
    {
        var sources = new[]
        {
            new LeaveBalanceSource(
                11, LeaveBalanceSourceKind.CurrentCycleEarned,
                3m, 0m, 0m, new DateOnly(2026, 1, 1), null),
            new LeaveBalanceSource(
                10, LeaveBalanceSourceKind.CarryForward,
                2m, 0m, 0m, new DateOnly(2025, 1, 1),
                new DateOnly(2026, 6, 30)),
            new LeaveBalanceSource(
                9, LeaveBalanceSourceKind.CarryForward,
                2m, 0m, 0m, new DateOnly(2025, 1, 1),
                new DateOnly(2026, 3, 31))
        };

        var plan = LeaveAllocationPlanner.PlanReservation(
            [new RequestedLeaveDate(new DateOnly(2026, 3, 31), 5m)],
            sources,
            businessDate: new DateOnly(2026, 2, 1),
            allowsNegativeBalance: false);

        Assert.Equal(
            new long[] { 9, 10, 11 },
            plan.Allocations.Select(allocation => allocation.SourceId));
        Assert.Equal(
            new decimal[] { 2m, 2m, 1m },
            plan.Allocations.Select(allocation => allocation.Units));
        Assert.Equal(0m, plan.UnfundedUnits);
    }

    [Fact]
    public void ReservationConsumesEarliestExpiryAcrossAllSourceKinds()
    {
        var plan = LeaveAllocationPlanner.PlanReservation(
            [new RequestedLeaveDate(new DateOnly(2026, 3, 20), 2m)],
            [
                new LeaveBalanceSource(
                    21, LeaveBalanceSourceKind.CurrentCycleEarned,
                    1m, 0m, 0m, new DateOnly(2026, 1, 1),
                    new DateOnly(2026, 3, 25)),
                new LeaveBalanceSource(
                    22, LeaveBalanceSourceKind.CarryForward,
                    1m, 0m, 0m, new DateOnly(2025, 1, 1),
                    new DateOnly(2026, 3, 31))
            ],
            businessDate: new DateOnly(2026, 3, 1),
            allowsNegativeBalance: false);

        Assert.Equal(
            new long[] { 21, 22 },
            plan.Allocations.Select(allocation => allocation.SourceId));
    }

    [Fact]
    public void ExpiredCarryForwardCannotBeUsedForLeaveDateAfterExpiry()
    {
        var plan = LeaveAllocationPlanner.PlanReservation(
            [new RequestedLeaveDate(new DateOnly(2026, 4, 1), 1m)],
            [
                new LeaveBalanceSource(
                    9, LeaveBalanceSourceKind.CarryForward,
                    2m, 0m, 0m, new DateOnly(2025, 1, 1),
                    new DateOnly(2026, 3, 31))
            ],
            businessDate: new DateOnly(2026, 3, 1),
            allowsNegativeBalance: true);

        Assert.Empty(plan.Allocations);
        Assert.Equal(1m, plan.UnfundedUnits);
    }

    [Fact]
    public void ReservationCanHonorSourceValidOnLeaveDateAfterItExpires()
    {
        var plan = LeaveAllocationPlanner.PlanReservation(
            [new RequestedLeaveDate(new DateOnly(2026, 3, 31), 1m)],
            [
                new LeaveBalanceSource(
                    9, LeaveBalanceSourceKind.CarryForward,
                    2m, 0m, 0m, new DateOnly(2025, 1, 1),
                    new DateOnly(2026, 3, 31))
            ],
            businessDate: new DateOnly(2026, 4, 5),
            allowsNegativeBalance: false);

        Assert.Single(plan.Allocations);
        Assert.Equal(9, plan.Allocations[0].SourceId);
    }

    [Fact]
    public void ExistingReservationsReduceAllocatableUnits()
    {
        var plan = LeaveAllocationPlanner.PlanReservation(
            [new RequestedLeaveDate(new DateOnly(2026, 5, 1), 2m)],
            [
                new LeaveBalanceSource(
                    15, LeaveBalanceSourceKind.CurrentCycleEarned,
                    3m, 0m, 2m, new DateOnly(2026, 1, 1), null)
            ],
            businessDate: new DateOnly(2026, 4, 1),
            allowsNegativeBalance: true);

        Assert.Single(plan.Allocations);
        Assert.Equal(1m, plan.Allocations[0].Units);
        Assert.Equal(1m, plan.UnfundedUnits);
    }

    private static LeavePolicy CreatePolicy(
        bool isAccrualEnabled,
        LeaveAccrualFrequency? accrualFrequency)
    {
        return new LeavePolicy(
            code: "ANNUAL",
            name: "Annual",
            version: 1,
            scope: LeavePolicyScope.Company,
            departmentId: null,
            designationId: null,
            employmentType: null,
            effectiveFrom: new DateOnly(2026, 1, 1),
            effectiveTo: null,
            dayCalculationMethod: LeaveDayCalculationMethod.CalendarDays,
            leaveWorkScheduleId: null,
            leaveHolidayCalendarId: null,
            allowsHalfDays: false,
            halfDayUnits: null,
            isAccrualEnabled: isAccrualEnabled,
            accrualFrequency: accrualFrequency,
            allowsCarryForward: false,
            carryForwardCap: null,
            carryForwardExpiryMonths: null,
            probationPeriodDays: null,
            allowsLeaveDuringProbation: false,
            allowsNegativeBalance: false);
    }
}
