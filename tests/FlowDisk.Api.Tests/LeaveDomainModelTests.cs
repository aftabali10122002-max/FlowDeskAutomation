using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class LeaveDomainModelTests
{
    [Fact]
    public void EmploymentProfileAllowsExistingUsersWithoutNewMetadata()
    {
        var profile = new EmployeeEmploymentProfile(
            userId: 12,
            designationId: null,
            employmentType: null,
            hireDate: null,
            effectiveFrom: new DateOnly(2026, 1, 1));

        Assert.Equal(12, profile.UserId);
        Assert.Null(profile.DesignationId);
        Assert.Null(profile.EmploymentType);
        Assert.Null(profile.HireDate);
    }

    [Fact]
    public void EmploymentProfileRejectsInvalidEffectiveRange()
    {
        Assert.Throws<ArgumentException>(() =>
            new EmployeeEmploymentProfile(
                userId: 12,
                designationId: null,
                employmentType: EmploymentType.FullTime,
                hireDate: new DateOnly(2020, 1, 1),
                effectiveFrom: new DateOnly(2026, 1, 2),
                effectiveTo: new DateOnly(2026, 1, 1)));
    }

    [Theory]
    [InlineData(LeavePolicyScope.Company, null, null, null, true)]
    [InlineData(LeavePolicyScope.Department, 2, null, null, true)]
    [InlineData(LeavePolicyScope.Designation, null, 3, null, true)]
    [InlineData(LeavePolicyScope.EmploymentType, null, null, EmploymentType.Contract, true)]
    [InlineData(LeavePolicyScope.Department, 2, 3, null, false)]
    [InlineData(LeavePolicyScope.Department, null, null, null, false)]
    [InlineData(LeavePolicyScope.Company, 2, null, null, false)]
    public void PolicyScopeRequiresExactlyOneMatchingTarget(
        LeavePolicyScope scope,
        int? departmentId,
        int? designationId,
        EmploymentType? employmentType,
        bool expected)
    {
        Assert.Equal(
            expected,
            LeavePolicy.HasValidScopeTarget(
                scope,
                departmentId,
                designationId,
                employmentType));
    }

    [Fact]
    public void PolicyRejectsMissingScheduleForWorkingDayCalculation()
    {
        Assert.Throws<ArgumentException>(() => CreatePolicy(
            dayCalculationMethod: LeaveDayCalculationMethod.WorkingDays,
            leaveWorkScheduleId: null));
    }

    [Fact]
    public void PolicyRejectsInvalidHalfDayAndAccrualSettings()
    {
        Assert.Throws<ArgumentException>(() => CreatePolicy(
            allowsHalfDays: true,
            halfDayUnits: 1m));
        Assert.Throws<ArgumentException>(() => CreatePolicy(
            isAccrualEnabled: true,
            accrualFrequency: null));
    }

    [Fact]
    public void NewPolicyStartsAsDraft()
    {
        var policy = CreatePolicy();

        Assert.Equal(LeavePolicyStatus.Draft, policy.Status);
        Assert.Null(policy.PublishedAtUtc);
        Assert.Null(policy.PublishedByUserId);
        Assert.Null(policy.SupersededAtUtc);
        Assert.Null(policy.SupersededByPolicyId);
    }

    [Fact]
    public void PublishingRecordsUtcActorAndMakesEntitlementsImmutable()
    {
        var policy = CreatePolicy();
        var entitlement = new LeavePolicyEntitlement(
            leavePolicyId: 1,
            leaveTypeId: 1,
            annualEntitlement: 15m);
        var publishedAt = new DateTime(
            2026, 10, 23, 18, 0, 0, DateTimeKind.Utc);

        policy.AddEntitlement(entitlement);
        policy.Publish(publishedByUserId: 5, publishedAt);

        Assert.Equal(LeavePolicyStatus.Published, policy.Status);
        Assert.Equal(publishedAt, policy.PublishedAtUtc);
        Assert.Equal(5, policy.PublishedByUserId);
        Assert.Single(policy.Entitlements);
        Assert.Throws<InvalidOperationException>(() =>
            policy.AddEntitlement(new LeavePolicyEntitlement(1, 2, 10m)));
        Assert.Throws<InvalidOperationException>(() =>
            policy.RemoveEntitlement(1));
        Assert.Throws<InvalidOperationException>(() =>
            policy.Publish(5, publishedAt));
    }

    [Fact]
    public void PublishingRequiresUtcTimestampAndValidActor()
    {
        var policy = CreatePolicy();

        Assert.Throws<ArgumentException>(() =>
            policy.Publish(5, DateTime.SpecifyKind(
                new DateTime(2026, 10, 23, 18, 0, 0),
                DateTimeKind.Local)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            policy.Publish(0, DateTime.UtcNow));
        Assert.Equal(LeavePolicyStatus.Draft, policy.Status);
    }

    [Fact]
    public async Task SupersedingRequiresNextPublishedVersionAfterItsEffectiveDate()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var admin = new User("Policy Admin", "policy-admin@test.local", "hash");
        admin.ChangeRole(UserRoles.Admin);
        var current = CreatePolicy();
        var future = CreatePolicy(
            version: 2,
            effectiveFrom: new DateOnly(2027, 1, 1));
        context.Users.Add(admin);
        context.LeavePolicies.AddRange(current, future);
        await context.SaveChangesAsync();

        var publishedAt = new DateTime(
            2026, 10, 23, 18, 0, 0, DateTimeKind.Utc);
        current.Publish(admin.Id, publishedAt);
        future.Publish(admin.Id, publishedAt);
        await context.SaveChangesAsync();

        Assert.Throws<InvalidOperationException>(() =>
            current.MarkSupersededBy(
                future,
                new DateTime(2026, 12, 31, 23, 59, 0, DateTimeKind.Utc)));

        var supersededAt = new DateTime(
            2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        current.MarkSupersededBy(future, supersededAt);
        await context.SaveChangesAsync();

        Assert.Equal(LeavePolicyStatus.Superseded, current.Status);
        Assert.Equal(future.Id, current.SupersededByPolicyId);
        Assert.Equal(supersededAt, current.SupersededAtUtc);
    }

    [Fact]
    public void EfModelDefinesLeaveRelationshipsConstraintsAndIndexes()
    {
        using var context = CreateContext();
        var designModel = context.GetService<IDesignTimeModel>().Model;
        var policy = RequiredEntity<LeavePolicy>(designModel);
        var entitlement = RequiredEntity<LeavePolicyEntitlement>(designModel);
        var assignment = RequiredEntity<LeavePolicyAssignment>(designModel);
        var employmentProfile =
            RequiredEntity<EmployeeEmploymentProfile>(designModel);
        var scheduleDay = RequiredEntity<LeaveWorkScheduleDay>(designModel);
        var holiday = RequiredEntity<LeaveHoliday>(designModel);
        var leaveTypes = RequiredEntity<LeaveType>(designModel);
        var designation = RequiredEntity<Designation>(designModel);

        Assert.Contains(
            policy.GetCheckConstraints(),
            constraint => constraint.Name ==
                "CK_LeavePolicies_ScopeTarget");
        Assert.Contains(
            policy.GetCheckConstraints(),
            constraint => constraint.Name == "CK_LeavePolicies_Lifecycle");
        AssertContainsForeignKey<LeavePolicy, User>(
            policy,
            nameof(LeavePolicy.PublishedByUserId));
        AssertContainsForeignKey<LeavePolicy, LeavePolicy>(
            policy,
            nameof(LeavePolicy.SupersededByPolicyId));
        AssertUniqueIndex(
            policy,
            nameof(LeavePolicy.SupersededByPolicyId));
        Assert.Contains(
            policy.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(LeavePolicy.Code),
                        nameof(LeavePolicy.Version)
                    ]) &&
                index.GetFilter() == "[Scope] = 1");
        Assert.Contains(
            policy.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(LeavePolicy.Code),
                        nameof(LeavePolicy.Version),
                        nameof(LeavePolicy.DepartmentId)
                    ]) &&
                index.GetFilter() ==
                    "[Scope] = 2 AND [DepartmentId] IS NOT NULL");
        Assert.Contains(
            policy.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(LeavePolicy.Code),
                        nameof(LeavePolicy.Version),
                        nameof(LeavePolicy.DesignationId)
                    ]) &&
                index.GetFilter() ==
                    "[Scope] = 3 AND [DesignationId] IS NOT NULL");
        Assert.Contains(
            policy.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(LeavePolicy.Code),
                        nameof(LeavePolicy.Version),
                        nameof(LeavePolicy.EmploymentType)
                    ]) &&
                index.GetFilter() ==
                    "[Scope] = 4 AND [EmploymentType] IS NOT NULL");
        Assert.Contains(
            policy.GetIndexes(),
            index => !index.IsUnique &&
                index.GetFilter() ==
                    "[Scope] = 2 AND [Status] IN (2, 3)");
        AssertUniqueIndex(leaveTypes, nameof(LeaveType.Code));
        AssertUniqueIndex(designation, nameof(Designation.Code));
        Assert.Contains(
            entitlement.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(LeavePolicyEntitlement.LeavePolicyId),
                        nameof(LeavePolicyEntitlement.LeaveTypeId)
                    ]));
        Assert.Contains(
            assignment.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(LeavePolicyAssignment.UserId),
                        nameof(LeavePolicyAssignment.EffectiveFrom)
                    ]));
        Assert.Contains(
            employmentProfile.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(EmployeeEmploymentProfile.UserId),
                        nameof(EmployeeEmploymentProfile.EffectiveFrom)
                    ]));
        Assert.Contains(
            scheduleDay.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(LeaveWorkScheduleDay.LeaveWorkScheduleId),
                        nameof(LeaveWorkScheduleDay.DayOfWeek)
                    ]));
        Assert.Contains(
            holiday.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(LeaveHoliday.LeaveHolidayCalendarId),
                        nameof(LeaveHoliday.Date)
                    ]));

        AssertForeignKey<LeavePolicyEntitlement, LeavePolicy>(entitlement);
        AssertForeignKey<LeavePolicyEntitlement, LeaveType>(entitlement);
        AssertForeignKey<LeavePolicyAssignment, User>(assignment);
        AssertForeignKey<LeavePolicyAssignment, LeavePolicy>(assignment);
        AssertForeignKey<EmployeeEmploymentProfile, User>(employmentProfile);
        AssertForeignKey<EmployeeEmploymentProfile, Designation>(
            employmentProfile);
        AssertForeignKey<LeaveWorkScheduleDay, LeaveWorkSchedule>(scheduleDay);
        AssertForeignKey<LeaveHoliday, LeaveHolidayCalendar>(holiday);
        AssertForeignKey<LeavePolicy, Department>(policy);
        AssertForeignKey<LeavePolicy, Designation>(policy);
        AssertForeignKey<LeavePolicy, LeaveWorkSchedule>(policy);
        AssertForeignKey<LeavePolicy, LeaveHolidayCalendar>(policy);

        Assert.Equal(4, leaveTypes.GetSeedData().Count());
        Assert.DoesNotContain(
            leaveTypes.GetSeedData(),
            seed => seed.Keys.Any(key =>
                key.Contains("Allowance", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void EmploymentProfileMetadataRemainsOptionalInEfModel()
    {
        using var context = CreateContext();
        var profile = RequiredEntity<EmployeeEmploymentProfile>(context.Model);

        Assert.True(profile.FindProperty(
            nameof(EmployeeEmploymentProfile.DesignationId))!.IsNullable);
        Assert.True(profile.FindProperty(
            nameof(EmployeeEmploymentProfile.EmploymentType))!.IsNullable);
        Assert.True(profile.FindProperty(
            nameof(EmployeeEmploymentProfile.HireDate))!.IsNullable);
        Assert.False(profile.FindProperty(
            nameof(EmployeeEmploymentProfile.EffectiveFrom))!.IsNullable);
        Assert.False(profile.FindProperty(
            nameof(EmployeeEmploymentProfile.UserId))!.IsNullable);
    }

    [Fact]
    public void RequiredCatalogAndEntitlementFieldsAreMappedAsRequired()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertRequired<LeaveType>(
            model,
            nameof(LeaveType.Code),
            nameof(LeaveType.DisplayName));
        AssertRequired<Designation>(
            model,
            nameof(Designation.Code),
            nameof(Designation.Name));
        AssertRequired<LeavePolicy>(
            model,
            nameof(LeavePolicy.Code),
            nameof(LeavePolicy.Name),
            nameof(LeavePolicy.Scope),
            nameof(LeavePolicy.EffectiveFrom));
        AssertRequired<LeavePolicyEntitlement>(
            model,
            nameof(LeavePolicyEntitlement.AnnualEntitlement));
    }

    private static LeavePolicy CreatePolicy(
        int version = 1,
        DateOnly? effectiveFrom = null,
        LeaveDayCalculationMethod dayCalculationMethod =
            LeaveDayCalculationMethod.CalendarDays,
        int? leaveWorkScheduleId = null,
        bool allowsHalfDays = false,
        decimal? halfDayUnits = null,
        bool isAccrualEnabled = false,
        LeaveAccrualFrequency? accrualFrequency = null)
    {
        return new LeavePolicy(
            code: "COMPANY-DEFAULT",
            name: "Company default",
            version: version,
            scope: LeavePolicyScope.Company,
            departmentId: null,
            designationId: null,
            employmentType: null,
            effectiveFrom: effectiveFrom ?? new DateOnly(2026, 1, 1),
            effectiveTo: null,
            dayCalculationMethod: dayCalculationMethod,
            leaveWorkScheduleId: leaveWorkScheduleId,
            leaveHolidayCalendarId: null,
            allowsHalfDays: allowsHalfDays,
            halfDayUnits: halfDayUnits,
            isAccrualEnabled: isAccrualEnabled,
            accrualFrequency: accrualFrequency,
            allowsCarryForward: false,
            carryForwardCap: null,
            carryForwardExpiryMonths: null,
            probationPeriodDays: null,
            allowsLeaveDuringProbation: false,
            allowsNegativeBalance: false);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        return new AppDbContext(options);
    }

    private static IEntityType RequiredEntity<TEntity>(IModel model)
        where TEntity : class
    {
        return model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} is missing from the EF model.");
    }

    private static void AssertForeignKey<TDependent, TPrincipal>(
        IEntityType dependent)
        where TDependent : class
        where TPrincipal : class
    {
        Assert.Contains(
            dependent.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(TPrincipal) &&
                foreignKey.DeleteBehavior == DeleteBehavior.NoAction);
    }

    private static void AssertContainsForeignKey<TDependent, TPrincipal>(
        IEntityType dependent,
        string propertyName)
        where TDependent : class
        where TPrincipal : class
    {
        Assert.Contains(
            dependent.GetForeignKeys(),
            foreignKey =>
                foreignKey.Properties.Select(property => property.Name)
                    .SequenceEqual([propertyName]) &&
                foreignKey.PrincipalEntityType.ClrType == typeof(TPrincipal) &&
                foreignKey.DeleteBehavior == DeleteBehavior.NoAction);
    }

    private static void AssertUniqueIndex(
        IEntityType entity,
        string propertyName)
    {
        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([propertyName]));
    }

    private static void AssertRequired<TEntity>(
        IModel model,
        params string[] propertyNames)
        where TEntity : class
    {
        var entity = RequiredEntity<TEntity>(model);
        foreach (var propertyName in propertyNames)
        {
            Assert.False(entity.FindProperty(propertyName)!.IsNullable);
        }
    }
}
