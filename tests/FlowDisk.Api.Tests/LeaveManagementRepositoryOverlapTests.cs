using FlowDesk.Application.DTOs;
using FlowDesk.Application.Errors;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using FlowDesk.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class LeaveManagementRepositoryOverlapTests
{
    [Fact]
    public void PolicyIndexes_AreScopedByTargetAndIncludeOverlapRanges()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new AppDbContext(options);
        var entity = context.Model.FindEntityType(typeof(LeavePolicy))!;
        var indexes = entity.GetIndexes();

        Assert.Contains(indexes, index =>
            !index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["EffectiveFrom", "EffectiveTo"]) &&
            index.GetFilter() ==
                "[Scope] = 1 AND [Status] IN (2, 3)");
        Assert.Contains(indexes, index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["Code", "Version"]) &&
            index.GetFilter() == "[Scope] = 1");
        Assert.Contains(indexes, index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["Code", "Version", "DepartmentId"]) &&
            index.GetFilter() ==
                "[Scope] = 2 AND [DepartmentId] IS NOT NULL");
        Assert.Contains(indexes, index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["Code", "Version", "DesignationId"]) &&
            index.GetFilter() ==
                "[Scope] = 3 AND [DesignationId] IS NOT NULL");
        Assert.Contains(indexes, index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["Code", "Version", "EmploymentType"]) &&
            index.GetFilter() ==
                "[Scope] = 4 AND [EmploymentType] IS NOT NULL");
        Assert.Contains(indexes, index =>
            !index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["DepartmentId", "EffectiveFrom", "EffectiveTo"]) &&
            index.GetFilter() ==
                "[Scope] = 2 AND [Status] IN (2, 3)");
        Assert.Contains(indexes, index =>
            !index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["DesignationId", "EffectiveFrom", "EffectiveTo"]) &&
            index.GetFilter() ==
                "[Scope] = 3 AND [Status] IN (2, 3)");
        Assert.Contains(indexes, index =>
            !index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["EmploymentType", "EffectiveFrom", "EffectiveTo"]) &&
            index.GetFilter() ==
                "[Scope] = 4 AND [Status] IN (2, 3)");
        Assert.Contains(
            context.Model.FindEntityType(
                typeof(LeavePolicyAssignment))!.GetIndexes(),
            index => !index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                        ["UserId", "EffectiveFrom", "EffectiveTo"]));
        Assert.Contains(
            context.Model.FindEntityType(
                typeof(EmployeeEmploymentProfile))!.GetIndexes(),
            index => !index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                        ["UserId", "EffectiveFrom", "EffectiveTo"]));
    }

    [Fact]
    public async Task CreateDraftAsync_AllowsSameCodeVersionForDifferentScopes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var department = new Department("Policy Scope Department");
        var designation = new Designation("POLICY-SCOPE", "Policy Scope");
        database.Context.Departments.Add(department);
        database.Context.Designations.Add(designation);
        await database.Context.SaveChangesAsync();
        var repository = new LeaveManagementRepository(database.Context);

        await repository.CreateDraftAsync(
            CreatePolicy("SHARED", LeavePolicyScope.Company),
            []);
        await repository.CreateDraftAsync(
            CreatePolicy(
                "SHARED",
                LeavePolicyScope.Department,
                departmentId: department.Id),
            []);
        await repository.CreateDraftAsync(
            CreatePolicy(
                "SHARED",
                LeavePolicyScope.Designation,
                designationId: designation.Id),
            []);
        await repository.CreateDraftAsync(
            CreatePolicy(
                "SHARED",
                LeavePolicyScope.EmploymentType,
                employmentType: EmploymentType.FullTime),
            []);

        Assert.Equal(4, await database.Context.LeavePolicies.CountAsync());
    }

    [Fact]
    public async Task CreateDraftAsync_RejectsDuplicateVersionWithinTheSameScope()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = new LeaveManagementRepository(database.Context);
        await repository.CreateDraftAsync(
            CreatePolicy("DUPLICATE", LeavePolicyScope.Company),
            []);

        await Assert.ThrowsAsync<LeaveOverlapException>(() =>
            repository.CreateDraftAsync(
                CreatePolicy("DUPLICATE", LeavePolicyScope.Company),
                []));

        Assert.Single(await database.Context.LeavePolicies.ToListAsync());
    }

    [Fact]
    public async Task PublishPolicyAsync_RejectsOverlappingPublishedScopeRanges()
    {
        await using var database = await TestDatabase.CreateAsync();
        var admin = await database.AddUserAsync(UserRoles.Admin);
        var repository = new LeaveManagementRepository(database.Context);
        var first = await CreatePublishedPolicyAsync(
            repository,
            "COMPANY-A",
            LeavePolicyScope.Company,
            admin.Id,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31));
        var overlapping = CreatePolicy(
            "COMPANY-B",
            LeavePolicyScope.Company,
            new DateOnly(2025, 12, 31));
        await repository.CreateDraftAsync(
            overlapping,
            CreateEntitlements());

        var error = await Assert.ThrowsAsync<LeaveOverlapException>(() =>
            repository.PublishPolicyAsync(
                overlapping.Id,
                admin.Id,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Contains("overlaps", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LeavePolicyStatus.Published, first.Status);
        Assert.Equal(LeavePolicyStatus.Draft, overlapping.Status);
    }

    [Fact]
    public async Task CreateAssignmentAsync_RejectsOverlapsAndAllowsAdjacentPeriods()
    {
        await using var database = await TestDatabase.CreateAsync();
        var admin = await database.AddUserAsync(UserRoles.Admin);
        var employee = await database.AddUserAsync(UserRoles.Employee);
        var manager = await database.AddUserAsync(UserRoles.Manager);
        var repository = new LeaveManagementRepository(database.Context);
        var policy = await CreatePublishedPolicyAsync(
            repository,
            "ASSIGNMENT",
            LeavePolicyScope.Company,
            admin.Id,
            new DateOnly(2025, 1, 1));
        await repository.CreateAssignmentAsync(
            new LeavePolicyAssignment(
                employee.Id,
                policy.Id,
                admin.Id,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 31)));

        await Assert.ThrowsAsync<LeaveNotFoundException>(() =>
                repository.CreateAssignmentAsync(
                    new LeavePolicyAssignment(
                        manager.Id,
                        policy.Id,
                        admin.Id,
                        new DateOnly(2026, 1, 1))));

        await Assert.ThrowsAsync<LeaveOverlapException>(() =>
            repository.CreateAssignmentAsync(
                new LeavePolicyAssignment(
                    employee.Id,
                    policy.Id,
                    admin.Id,
                    new DateOnly(2026, 1, 31),
                    new DateOnly(2026, 2, 28))));

        await repository.CreateAssignmentAsync(
            new LeavePolicyAssignment(
                employee.Id,
                policy.Id,
                admin.Id,
                new DateOnly(2026, 2, 1)));

        Assert.Equal(
            2,
            await database.Context.LeavePolicyAssignments.CountAsync());
    }

    [Fact]
    public async Task CreateEmploymentProfileAsync_RejectsOverlapsAndAllowsAdjacentPeriods()
    {
        await using var database = await TestDatabase.CreateAsync();
        var employee = await database.AddUserAsync(UserRoles.Employee);
        await InsertEmploymentProfileAsync(
            database.Context,
            employee.Id,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31));
        var repository = new LeaveManagementRepository(database.Context);

        await Assert.ThrowsAsync<LeaveOverlapException>(() =>
            repository.CreateEmploymentProfileAsync(
                CreateProfile(
                    employee.Id,
                    new DateOnly(2026, 1, 31),
                    new DateOnly(2026, 2, 28))));

        var adjacentError = await Assert.ThrowsAsync<DbUpdateException>(() =>
            repository.CreateEmploymentProfileAsync(
                CreateProfile(employee.Id, new DateOnly(2026, 2, 1))));
        Assert.Contains(
            "RowVersion",
            adjacentError.InnerException?.Message ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            1,
            await database.Context.EmployeeEmploymentProfiles.CountAsync());
    }

    private static async Task<LeavePolicy> CreatePublishedPolicyAsync(
        LeaveManagementRepository repository,
        string code,
        LeavePolicyScope scope,
        int adminUserId,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo = null)
    {
        var policy = CreatePolicy(code, scope, effectiveFrom, effectiveTo);
        await repository.CreateDraftAsync(policy, CreateEntitlements());
        await repository.PublishPolicyAsync(
            policy.Id,
            adminUserId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        return policy;
    }

    private static LeavePolicy CreatePolicy(
        string code,
        LeavePolicyScope scope,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null,
        int? departmentId = null,
        int? designationId = null,
        EmploymentType? employmentType = null)
    {
        return new LeavePolicy(
            code,
            code,
            1,
            scope,
            departmentId,
            designationId,
            employmentType,
            effectiveFrom ?? new DateOnly(2025, 1, 1),
            effectiveTo,
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

    private static IReadOnlyList<LeavePolicyEntitlementInput>
        CreateEntitlements()
    {
        return
        [
            new(1, 20m),
            new(2, 20m),
            new(3, 20m),
            new(4, 0m)
        ];
    }

    private static EmployeeEmploymentProfile CreateProfile(
        int employeeUserId,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo = null)
    {
        return new EmployeeEmploymentProfile(
            employeeUserId,
            null,
            EmploymentType.FullTime,
            new DateOnly(2020, 1, 1),
            effectiveFrom,
            effectiveTo);
    }

    private static Task<int> InsertEmploymentProfileAsync(
        AppDbContext context,
        int userId,
        DateOnly effectiveFrom,
        DateOnly effectiveTo)
    {
        return context.Database.ExecuteSqlRawAsync(
            "INSERT INTO [EmployeeEmploymentProfiles] " +
            "([UserId], [DesignationId], [EmploymentType], [HireDate], " +
            "[EffectiveFrom], [EffectiveTo], [RowVersion]) " +
            "VALUES ({0}, NULL, {1}, {2}, {3}, {4}, {5})",
            userId,
            (int)EmploymentType.FullTime,
            "2020-01-01",
            effectiveFrom.ToString("yyyy-MM-dd"),
            effectiveTo.ToString("yyyy-MM-dd"),
            new byte[8]);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestDatabase(SqliteConnection connection, AppDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        public AppDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public async Task<User> AddUserAsync(string role)
        {
            var suffix = Guid.NewGuid().ToString("N");
            var user = new User(
                $"Overlap test {suffix}",
                $"{suffix}@example.test",
                "test-hash");
            if (role != UserRoles.Employee)
            {
                user.ChangeRole(role);
            }

            Context.Users.Add(user);
            await Context.SaveChangesAsync();
            return user;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
