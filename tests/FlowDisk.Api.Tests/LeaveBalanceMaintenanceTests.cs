using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class LeaveBalanceMaintenanceTests
{
    [Fact]
    public async Task AnnualAccrualPostsFullCalendarYearGrantOnce()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        await AddPolicyAsync(
            factory,
            admin.Id,
            new DateOnly(2026, 1, 1),
            accrualFrequency: LeaveAccrualFrequency.Annually);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<ILeaveAccountingRepository>();
        var processingTime = new DateTime(
            2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(
            4,
            await repository.ProcessDueAccrualsAsync(
                processingTime,
                "UTC"));
        Assert.Equal(
            0,
            await repository.ProcessDueAccrualsAsync(
                processingTime,
                "UTC"));

        var context = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        var annualEntries = await context.LeaveBalanceLedgerEntries
            .Where(entry =>
                entry.EmployeeUserId == employee.Id &&
                entry.EntryType == LeaveLedgerEntryType.Accrual)
            .ToListAsync();
        Assert.Equal(4, annualEntries.Count);
        Assert.All(annualEntries, entry => Assert.Equal(12m, entry.Units));
        Assert.All(annualEntries, entry =>
            Assert.Equal(new DateOnly(2026, 1, 1), entry.EffectiveDate));
    }

    [Fact]
    public async Task AnnualAccrualProratesMidYearHireAndUsesLeapCalendarYear()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var fullYearEmployee = await factory.AddUserAsync();
        var midYearEmployee = await factory.AddUserAsync();
        await AddPolicyAsync(
            factory,
            admin.Id,
            new DateOnly(2028, 1, 1),
            accrualFrequency: LeaveAccrualFrequency.Annually);
        await AddEmploymentProfileAsync(
            factory,
            fullYearEmployee.Id,
            new DateOnly(2028, 1, 1),
            new DateOnly(2028, 1, 1));
        await AddEmploymentProfileAsync(
            factory,
            midYearEmployee.Id,
            new DateOnly(2028, 7, 1),
            new DateOnly(2028, 1, 1));

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<ILeaveAccountingRepository>();
        Assert.Equal(
            8,
            await repository.ProcessDueAccrualsAsync(
                new DateTime(2028, 7, 1, 12, 0, 0, DateTimeKind.Utc),
                "UTC"));

        var context = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        var annualEntries = await context.LeaveBalanceLedgerEntries
            .Where(entry =>
                entry.EntryType == LeaveLedgerEntryType.Accrual &&
                entry.LeaveTypeId == 1)
            .ToListAsync();
        var fullYearGrant = Assert.Single(
            annualEntries,
            entry => entry.EmployeeUserId == fullYearEmployee.Id);
        Assert.Equal(12m, fullYearGrant.Units);
        Assert.Equal(new DateOnly(2028, 1, 1), fullYearGrant.EffectiveDate);
        var midYearGrant = Assert.Single(
            annualEntries,
            entry => entry.EmployeeUserId == midYearEmployee.Id);
        Assert.Equal(6.03m, midYearGrant.Units);
        Assert.Equal(new DateOnly(2028, 7, 1), midYearGrant.EffectiveDate);
    }

    [Fact]
    public async Task AnnualAccrualProratesMidYearPolicyEligibility()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        await AddPolicyAsync(
            factory,
            admin.Id,
            new DateOnly(2026, 1, 1),
            effectiveTo: new DateOnly(2026, 6, 30),
            accrualFrequency: LeaveAccrualFrequency.Annually);
        await AddPolicyAsync(
            factory,
            admin.Id,
            new DateOnly(2026, 7, 1),
            accrualFrequency: LeaveAccrualFrequency.Annually,
            annualEntitlement: 24m);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<ILeaveAccountingRepository>();
        Assert.Equal(
            4,
            await repository.ProcessDueAccrualsAsync(
                new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                "UTC"));
        Assert.Equal(
            4,
            await repository.ProcessDueAccrualsAsync(
                new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
                "UTC"));

        var context = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        var annualEntries = await context.LeaveBalanceLedgerEntries
            .Where(entry =>
                entry.EmployeeUserId == employee.Id &&
                entry.LeaveTypeId == 1 &&
                entry.EntryType == LeaveLedgerEntryType.Accrual)
            .OrderBy(entry => entry.EffectiveDate)
            .ToListAsync();
        Assert.Equal(2, annualEntries.Count);
        Assert.Equal(5.95m, annualEntries[0].Units);
        Assert.Equal(new DateOnly(2026, 1, 1), annualEntries[0].EffectiveDate);
        Assert.Equal(12.10m, annualEntries[1].Units);
        Assert.Equal(new DateOnly(2026, 7, 1), annualEntries[1].EffectiveDate);
        Assert.Equal(18.05m, annualEntries.Sum(entry => entry.Units));
    }

    [Fact]
    public async Task AnnualAccrualStartsAfterMidYearProbationCompletion()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        await AddPolicyAsync(
            factory,
            admin.Id,
            new DateOnly(2026, 1, 1),
            accrualFrequency: LeaveAccrualFrequency.Annually,
            probationPeriodDays: 60,
            allowsLeaveDuringProbation: false);
        await AddEmploymentProfileAsync(
            factory,
            employee.Id,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1));

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<ILeaveAccountingRepository>();
        Assert.Equal(
            4,
            await repository.ProcessDueAccrualsAsync(
                new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc),
                "UTC"));

        var context = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        var grant = await context.LeaveBalanceLedgerEntries
            .SingleAsync(entry =>
                entry.EmployeeUserId == employee.Id &&
                entry.LeaveTypeId == 1 &&
                entry.EntryType == LeaveLedgerEntryType.Accrual);
        Assert.Equal(10.03m, grant.Units);
        Assert.Equal(new DateOnly(2026, 3, 2), grant.EffectiveDate);
    }

    [Fact]
    public async Task PayPeriodAccrualPostsCompletedPeriodsOnce()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        int calendarId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            var calendar = new LeavePayrollCalendar(
                $"PAY-{Guid.NewGuid():N}",
                "Test payroll calendar",
                "UTC",
                expectedPeriodsInEntitlementYear: 24);
            context.LeavePayrollCalendars.Add(calendar);
            await context.SaveChangesAsync();
            calendarId = calendar.Id;
            context.LeavePayPeriods.AddRange(
                new LeavePayPeriod(
                    calendarId,
                    new DateOnly(2026, 1, 1),
                    new DateOnly(2026, 1, 15),
                    new DateOnly(2026, 1, 16),
                    2026,
                    1),
                new LeavePayPeriod(
                    calendarId,
                    new DateOnly(2026, 1, 16),
                    new DateOnly(2026, 1, 31),
                    new DateOnly(2026, 2, 1),
                    2026,
                    2));
            await context.SaveChangesAsync();
        }

        await AddPolicyAsync(
            factory,
            admin.Id,
            new DateOnly(2026, 1, 1),
            accrualFrequency: LeaveAccrualFrequency.PerPayPeriod,
            leavePayrollCalendarId: calendarId);

        using var runScope = factory.Services.CreateScope();
        var repository = runScope.ServiceProvider
            .GetRequiredService<ILeaveAccountingRepository>();
        var processingTime = new DateTime(
            2026, 2, 5, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(
            8,
            await repository.ProcessDueAccrualsAsync(
                processingTime,
                "UTC"));
        Assert.Equal(
            0,
            await repository.ProcessDueAccrualsAsync(
                processingTime,
                "UTC"));

        var contextAfterRun = runScope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        var annualLeaveEntries = await contextAfterRun
            .LeaveBalanceLedgerEntries
            .Where(entry =>
                entry.EmployeeUserId == employee.Id &&
                entry.LeaveTypeId == 1 &&
                entry.EntryType == LeaveLedgerEntryType.Accrual)
            .OrderBy(entry => entry.EffectiveDate)
            .ToListAsync();
        Assert.Equal(2, annualLeaveEntries.Count);
        Assert.All(annualLeaveEntries, entry => Assert.Equal(0.5m, entry.Units));
        Assert.All(annualLeaveEntries, entry => Assert.NotNull(entry.PayPeriodId));
    }

    [Fact]
    public async Task MonthlyAccrualUsesPolicyEffectiveOnEachDay()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        await AddPolicyAsync(
            factory,
            admin.Id,
            new DateOnly(2026, 1, 1),
            accrualFrequency: LeaveAccrualFrequency.Monthly,
            effectiveTo: new DateOnly(2026, 6, 30));
        await AddPolicyAsync(
            factory,
            admin.Id,
            new DateOnly(2026, 7, 1),
            accrualFrequency: LeaveAccrualFrequency.Monthly,
            annualEntitlement: 24m);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<ILeaveAccountingRepository>();
        Assert.Equal(
            48,
            await repository.ProcessDueAccrualsAsync(
                new DateTime(2026, 12, 31, 12, 0, 0, DateTimeKind.Utc),
                "UTC"));

        var context = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        var annualEntries = await context.LeaveBalanceLedgerEntries
            .Where(entry =>
                entry.EmployeeUserId == employee.Id &&
                entry.LeaveTypeId == 1 &&
                entry.EntryType == LeaveLedgerEntryType.Accrual)
            .ToListAsync();
        Assert.Equal(18m, annualEntries.Sum(entry => entry.Units));
        Assert.Equal(12, annualEntries.Count);
    }

    [Fact]
    public async Task MonthlyAccrualPostsClosedPeriodsOnce()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        await AddPolicyAsync(
            factory,
            admin.Id,
            new DateOnly(2026, 1, 1),
            accrualFrequency: LeaveAccrualFrequency.Monthly);

        var processingTime = new DateTime(
            2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        int firstPass;
        int secondPass;
        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<ILeaveAccountingRepository>();
            firstPass = await repository.ProcessDueAccrualsAsync(
                processingTime,
                "UTC");
            secondPass = await repository.ProcessDueAccrualsAsync(
                processingTime,
                "UTC");
        }

        Assert.Equal(36, firstPass);
        Assert.Equal(0, secondPass);

        using var verifyScope = factory.Services.CreateScope();
        var context = verifyScope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        var entries = await context.LeaveBalanceLedgerEntries
            .Where(entry =>
                entry.EmployeeUserId == employee.Id &&
                entry.EntryType == LeaveLedgerEntryType.Accrual)
            .ToListAsync();
        Assert.Equal(36, entries.Count);
        Assert.All(entries, entry => Assert.Equal(1m, entry.Units));
        Assert.All(entries, entry =>
            Assert.Equal(
                new DateOnly(entry.EffectiveDate.Year, entry.EffectiveDate.Month, 1),
                entry.EffectiveDate));
        Assert.Contains(entries, entry =>
            entry.EffectiveDate == new DateOnly(2026, 2, 1));
    }

    [Fact]
    public async Task CarryForwardCapAndExpiryAreAppendOnlyAndIdempotent()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        var policy = await AddPolicyAsync(
            factory,
            admin.Id,
            new DateOnly(2020, 1, 1),
            allowsCarryForward: true,
            carryForwardCap: 5m,
            carryForwardExpiryMonths: 6);
        var earnedAtUtc = new DateTime(
            2024, 12, 31, 12, 0, 0, DateTimeKind.Utc);
        var accrual = new LeaveBalanceLedgerEntry(
            employee.Id,
            leaveTypeId: 1,
            policy.Id,
            requestId: null,
            payPeriodId: null,
            LeaveLedgerEntryType.Accrual,
            new DateOnly(2024, 12, 31),
            10m,
            $"test-accrual:{Guid.NewGuid():N}",
            earnedAtUtc);
        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<ILeaveAccountingRepository>();
            var maintenanceContext = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            Assert.True(await repository.TryAppendLedgerWriteAsync(
                new FlowDesk.Application.DTOs.LeaveLedgerWrite(
                    accrual,
                    [])));
            var request = new ApprovalRequest(
                "Already used leave",
                amount: null,
                createdByUserId: employee.Id,
                departmentId: null,
                requireAmount: false);
            maintenanceContext.ApprovalRequests.Add(request);
            await maintenanceContext.SaveChangesAsync();
            var used = new LeaveBalanceLedgerEntry(
                employee.Id,
                leaveTypeId: 1,
                leavePolicyId: policy.Id,
                requestId: request.Id,
                payPeriodId: null,
                LeaveLedgerEntryType.Used,
                new DateOnly(2024, 12, 31),
                3m,
                $"test-used:{Guid.NewGuid():N}",
                earnedAtUtc);
            Assert.True(await repository.TryAppendLedgerWriteAsync(
                new FlowDesk.Application.DTOs.LeaveLedgerWrite(
                    used,
                    [
                        new FlowDesk.Application.DTOs.LeaveLedgerAllocationInput(
                            accrual.Id,
                            new DateOnly(2024, 12, 31),
                            3m)
                    ])));

            var januaryProcessing = new DateTime(
                2025, 1, 2, 12, 0, 0, DateTimeKind.Utc);
            Assert.Equal(
                2,
                await repository.ProcessDueCarryForwardAndExpiryAsync(
                    januaryProcessing,
                    "UTC"));
            Assert.Equal(
                0,
                await repository.ProcessDueCarryForwardAndExpiryAsync(
                    januaryProcessing.AddDays(1),
                    "UTC"));
            var decemberBalance = await repository.GetBalancesAsync(
                employee.Id,
                new DateOnly(2024, 12, 31));
            var decemberAnnual = Assert.Single(
                decemberBalance,
                balance => balance.LeaveTypeId == 1);
            Assert.Equal(7m, decemberAnnual.Available);

            var carryForwardEntry = await maintenanceContext
                .LeaveBalanceLedgerEntries
                .SingleAsync(entry =>
                    entry.EmployeeUserId == employee.Id &&
                    entry.EntryType == LeaveLedgerEntryType.CarryForward);
            Assert.Equal(new DateOnly(2025, 1, 1), carryForwardEntry.EffectiveDate);
            Assert.Equal(5m, carryForwardEntry.Units);
            Assert.Equal(new DateOnly(2025, 6, 30), carryForwardEntry.ExpiresOn);

            var januaryBalance = await repository.GetBalancesAsync(
                employee.Id,
                new DateOnly(2025, 1, 1));
            var januaryAnnual = Assert.Single(
                januaryBalance,
                balance => balance.LeaveTypeId == 1);
            Assert.Equal(5m, januaryAnnual.Available);
            Assert.Equal(5m, januaryAnnual.CarryForward);
            Assert.Equal(2m, januaryAnnual.Expired);

            var julyProcessing = new DateTime(
                2025, 7, 1, 12, 0, 0, DateTimeKind.Utc);
            Assert.Equal(
                1,
                await repository.ProcessDueCarryForwardAndExpiryAsync(
                    julyProcessing,
                    "UTC"));
            Assert.Equal(
                0,
                await repository.ProcessDueCarryForwardAndExpiryAsync(
                    julyProcessing.AddDays(1),
                    "UTC"));
            var julyBalance = await repository.GetBalancesAsync(
                employee.Id,
                new DateOnly(2025, 7, 2));
            var julyAnnual = Assert.Single(
                julyBalance,
                balance => balance.LeaveTypeId == 1);
            Assert.Equal(0m, julyAnnual.Available);
            Assert.Equal(7m, julyAnnual.Expired);
        }

        using var verifyScope = factory.Services.CreateScope();
        var context = verifyScope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        Assert.Equal(
            1,
            await context.LeaveBalanceLedgerEntries.CountAsync(entry =>
                entry.EntryType == LeaveLedgerEntryType.CarryForward));
        Assert.Equal(
            2,
            await context.LeaveBalanceLedgerEntries.CountAsync(entry =>
                entry.EntryType == LeaveLedgerEntryType.Expiry));
    }

    [Fact]
    public async Task ExpiredUnreservedUnitsAreExcludedBeforeMaintenancePostsExpiry()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var employee = await factory.AddUserAsync();
        var expiryDate = new DateOnly(2026, 5, 31);
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<ILeaveAccountingRepository>();
        var accrual = new LeaveBalanceLedgerEntry(
            employee.Id,
            leaveTypeId: 1,
            leavePolicyId: null,
            requestId: null,
            payPeriodId: null,
            LeaveLedgerEntryType.Accrual,
            new DateOnly(2026, 1, 1),
            5m,
            $"expiring-accrual:{Guid.NewGuid():N}",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            expiresOn: expiryDate);
        Assert.True(await repository.TryAppendLedgerWriteAsync(
            new FlowDesk.Application.DTOs.LeaveLedgerWrite(accrual, [])));

        var context = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        var request = new ApprovalRequest(
            "Reserved leave",
            amount: null,
            createdByUserId: employee.Id,
            departmentId: null,
            requireAmount: false);
        context.ApprovalRequests.Add(request);
        await context.SaveChangesAsync();
        var reservation = new LeaveBalanceLedgerEntry(
            employee.Id,
            leaveTypeId: 1,
            leavePolicyId: null,
            requestId: request.Id,
            payPeriodId: null,
            LeaveLedgerEntryType.Reservation,
            new DateOnly(2026, 5, 1),
            2m,
            $"expiring-reservation:{Guid.NewGuid():N}",
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.True(await repository.TryAppendLedgerWriteAsync(
            new FlowDesk.Application.DTOs.LeaveLedgerWrite(
                reservation,
                [
                    new FlowDesk.Application.DTOs.LeaveLedgerAllocationInput(
                        accrual.Id,
                        expiryDate,
                        2m)
                ])));

        var beforeMaintenance = await repository.GetBalancesAsync(
            employee.Id,
            new DateOnly(2026, 6, 1));
        var balance = Assert.Single(
            beforeMaintenance,
            item => item.LeaveTypeId == 1);
        Assert.Equal(5m, balance.Earned);
        Assert.Equal(2m, balance.Pending);
        Assert.Equal(0m, balance.Expired);
        Assert.Equal(0m, balance.Available);
    }

    private static async Task<LeavePolicy> AddPolicyAsync(
        ApiFactory factory,
        int adminUserId,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo = null,
        LeaveAccrualFrequency? accrualFrequency = null,
        bool allowsCarryForward = false,
        decimal? carryForwardCap = null,
        int? carryForwardExpiryMonths = null,
        int? leavePayrollCalendarId = null,
        decimal annualEntitlement = 12m,
        int? probationPeriodDays = null,
        bool allowsLeaveDuringProbation = true)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        var policy = new LeavePolicy(
            $"MAINT-{Guid.NewGuid():N}",
            "Maintenance test policy",
            1,
            LeavePolicyScope.Company,
            null,
            null,
            null,
            effectiveFrom,
            effectiveTo,
            LeaveDayCalculationMethod.CalendarDays,
            null,
            null,
            allowsHalfDays: false,
            halfDayUnits: null,
            isAccrualEnabled: accrualFrequency.HasValue,
            accrualFrequency,
            allowsCarryForward,
            carryForwardCap,
            carryForwardExpiryMonths,
            probationPeriodDays,
            allowsLeaveDuringProbation,
            allowsNegativeBalance: false,
            leavePayrollCalendarId: leavePayrollCalendarId);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();
        foreach (var leaveTypeId in new[] { 1, 2, 3, 4 })
        {
            policy.AddEntitlement(new LeavePolicyEntitlement(
                policy.Id,
                leaveTypeId,
                annualEntitlement));
        }

        policy.Publish(adminUserId, DateTime.UtcNow);
        await context.SaveChangesAsync();
        return policy;
    }

    private static async Task AddEmploymentProfileAsync(
        ApiFactory factory,
        int employeeUserId,
        DateOnly hireDate,
        DateOnly effectiveFrom)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO [EmployeeEmploymentProfiles] " +
            "([UserId], [DesignationId], [EmploymentType], [HireDate], " +
            "[EffectiveFrom], [EffectiveTo], [RowVersion]) " +
            "VALUES ({0}, NULL, NULL, {1}, {2}, NULL, {3})",
            employeeUserId,
            hireDate.ToString("yyyy-MM-dd"),
            effectiveFrom.ToString("yyyy-MM-dd"),
            new byte[8]);
    }
}
