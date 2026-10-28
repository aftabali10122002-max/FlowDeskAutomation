using FlowDesk.Application.Services;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class LeavePolicyResolutionTests
{
    [Fact]
    public async Task ResolutionUsesApprovedPrecedenceAndExplicitAssignment()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var admin = new User("Leave Admin", "leave-admin@test.local", "hash");
        admin.ChangeRole(UserRoles.Admin);
        var employee = new User("Leave Employee", "leave-employee@test.local", "hash");
        var department = new Department("Leave Resolution");
        var designation = new Designation("ENGINEER", "Engineer");
        context.Users.AddRange(admin, employee);
        context.Departments.Add(department);
        context.Designations.Add(designation);
        await context.SaveChangesAsync();
        employee.ChangeDepartment(department.Id);
        await context.SaveChangesAsync();

        var company = CreatePolicy(
            "COMPANY",
            LeavePolicyScope.Company,
            effectiveFrom: new DateOnly(2020, 1, 1));
        var departmentPolicy = CreatePolicy(
            "DEPARTMENT",
            LeavePolicyScope.Department,
            departmentId: department.Id,
            effectiveFrom: new DateOnly(2020, 1, 1));
        var employment = CreatePolicy(
            "EMPLOYMENT",
            LeavePolicyScope.EmploymentType,
            employmentType: EmploymentType.FullTime,
            effectiveFrom: new DateOnly(2020, 1, 1));
        var designationPolicy = CreatePolicy(
            "DESIGNATION",
            LeavePolicyScope.Designation,
            designationId: designation.Id,
            effectiveFrom: new DateOnly(2020, 1, 1));
        context.LeavePolicies.AddRange(
            company,
            departmentPolicy,
            employment,
            designationPolicy);
        await context.SaveChangesAsync();
        await PublishPoliciesAsync(
            context,
            admin.Id,
            company,
            departmentPolicy,
            employment,
            designationPolicy);

        var profile = new EmployeeEmploymentProfile(
            employee.Id,
            designation.Id,
            EmploymentType.FullTime,
            new DateOnly(2020, 1, 1),
            new DateOnly(2020, 1, 1));

        var policies = await context.LeavePolicies
            .Include(policy => policy.Entitlements)
            .AsNoTracking()
            .ToListAsync();
        var resolved = LeavePolicyResolver.Resolve(
            employee,
            [profile],
            [],
            policies,
            leaveTypeId: 1,
            new DateOnly(2026, 1, 1));
        Assert.Equal(
            LeavePolicyResolutionTier.Designation,
            resolved.ResolvedScope);
        Assert.Equal(designationPolicy.Id, resolved.Policy.Id);

        var explicitAssignment = new LeavePolicyAssignment(
            employee.Id,
            company.Id,
            admin.Id,
            new DateOnly(2020, 1, 1));
        resolved = LeavePolicyResolver.Resolve(
            employee,
            [profile],
            [explicitAssignment],
            policies,
            leaveTypeId: 1,
            new DateOnly(2026, 1, 1));
        Assert.Equal(
            LeavePolicyResolutionTier.EmployeeAssignment,
            resolved.ResolvedScope);
        Assert.Equal(company.Id, resolved.Policy.Id);

        resolved = LeavePolicyResolver.Resolve(
            employee,
            [],
            [],
            policies.Where(policy =>
                policy.Scope is LeavePolicyScope.Department or
                    LeavePolicyScope.Company),
            leaveTypeId: 1,
            new DateOnly(2026, 1, 1));
        Assert.Equal(
            LeavePolicyResolutionTier.Department,
            resolved.ResolvedScope);
    }

    [Fact]
    public async Task OverlappingPoliciesAtSameTierAreAmbiguous()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var admin = new User("Overlap Admin", "overlap-admin@test.local", "hash");
        admin.ChangeRole(UserRoles.Admin);
        var department = new Department("Overlap Department");
        var employee = new User("Overlap Employee", "overlap-employee@test.local", "hash");
        context.Users.AddRange(admin, employee);
        context.Departments.Add(department);
        await context.SaveChangesAsync();
        employee.ChangeDepartment(department.Id);
        await context.SaveChangesAsync();

        var first = CreatePolicy(
            "DEPT-A",
            LeavePolicyScope.Department,
            departmentId: department.Id,
            effectiveFrom: new DateOnly(2020, 1, 1));
        var second = CreatePolicy(
            "DEPT-B",
            LeavePolicyScope.Department,
            departmentId: department.Id,
            effectiveFrom: new DateOnly(2025, 1, 1));
        context.LeavePolicies.AddRange(first, second);
        await context.SaveChangesAsync();
        await PublishPoliciesAsync(context, admin.Id, first, second);

        var policies = await context.LeavePolicies
            .Include(policy => policy.Entitlements)
            .AsNoTracking()
            .ToListAsync();
        var error = Assert.Throws<LeavePolicyConfigurationException>(() =>
            LeavePolicyResolver.Resolve(
                employee,
                [],
                [],
                policies,
                leaveTypeId: 1,
                new DateOnly(2026, 1, 1)));
        Assert.Contains("Multiple effective", error.Message);
    }

    private static async Task PublishPoliciesAsync(
        AppDbContext context,
        int adminUserId,
        params LeavePolicy[] policies)
    {
        foreach (var policy in policies)
        {
            policy.AddEntitlement(new LeavePolicyEntitlement(
                policy.Id,
                1,
                20m));
            policy.Publish(adminUserId, DateTime.UtcNow);
        }

        await context.SaveChangesAsync();
    }

    private static LeavePolicy CreatePolicy(
        string code,
        LeavePolicyScope scope,
        int? departmentId = null,
        int? designationId = null,
        EmploymentType? employmentType = null,
        DateOnly? effectiveFrom = null)
    {
        return new LeavePolicy(
            code,
            code,
            1,
            scope,
            departmentId,
            designationId,
            employmentType,
            effectiveFrom ?? new DateOnly(2020, 1, 1),
            null,
            LeaveDayCalculationMethod.CalendarDays,
            null,
            null,
            false,
            null,
            false,
            null,
            false,
            null,
            null,
            null,
            true,
            false);
    }
}
