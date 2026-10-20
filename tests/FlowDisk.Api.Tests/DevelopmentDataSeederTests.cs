using System.Net;
using System.Security.Cryptography;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using FlowDesk.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class DevelopmentDataSeederTests
{
    [Fact]
    public async Task SeedAsync_CreatesExpectedUsersRequestsAndAssignments()
    {
        await using var database = await CreateDatabaseAsync();
        var password = CreateTestPassword();
        var passwordHasher = new PasswordHasherService();
        var seeder = new DevelopmentDataSeeder(database.Context, passwordHasher);

        var result = await seeder.SeedAsync(password);

        Assert.Equal(DevelopmentSeedResult.Inserted, result);

        var emails = DevelopmentDataSeeder.GetSeedAccountEmails();
        var users = await database.Context.Users
            .AsNoTracking()
            .Include(x => x.Department)
            .Where(x => emails.Contains(x.Email))
            .ToListAsync();

        Assert.Equal(24, users.Count);
        Assert.Equal(18, users.Count(x => x.Role == UserRoles.Employee));
        Assert.Equal(6, users.Count(x => x.Role == UserRoles.Manager));
        Assert.All(users, user =>
        {
            Assert.NotEqual(UserRoles.Admin, user.Role);
            Assert.True(passwordHasher.Verify(password, user.PasswordHash));
            Assert.NotEqual(password, user.PasswordHash);
            Assert.NotEqual(DateTimeKind.Local, user.CreatedAtUtc.Kind);
        });
        Assert.Equal(
            users.Count,
            users.Select(x => x.PasswordHash).Distinct().Count());

        var requests = await database.Context.ApprovalRequests
            .AsNoTracking()
            .Include(x => x.CreatedByUser)
            .Include(x => x.Department)
            .Include(x => x.Assignments)
                .ThenInclude(x => x.ApproverUser)
            .ToListAsync();

        Assert.Equal(100, requests.Count);
        Assert.Equal(30, requests.Count(x => x.Status == RequestStatus.Pending));
        Assert.Equal(40, requests.Count(x => x.Status == RequestStatus.Approved));
        Assert.Equal(20, requests.Count(x => x.Status == RequestStatus.Rejected));
        Assert.Equal(10, requests.Count(x => x.Status == RequestStatus.Cancelled));
        Assert.All(requests, request =>
        {
            Assert.NotNull(request.CreatedByUser);
            Assert.NotNull(request.Department);
            Assert.Equal(UserRoles.Employee, request.CreatedByUser.Role);
            Assert.Equal(request.DepartmentId, request.CreatedByUser.DepartmentId);
            Assert.True(
                request.CreatedAtUtc >= request.CreatedByUser.CreatedAtUtc);

            var assignment = Assert.Single(request.Assignments);
            Assert.Equal(UserRoles.Manager, assignment.ApproverUser.Role);
            Assert.Equal(request.DepartmentId, assignment.ApproverUser.DepartmentId);
            Assert.True(
                assignment.ApproverUser.CreatedAtUtc <= request.CreatedAtUtc);
            Assert.True(assignment.AssignedAtUtc >= request.CreatedAtUtc);
            Assert.NotEqual(DateTimeKind.Local, request.CreatedAtUtc.Kind);
            Assert.NotEqual(DateTimeKind.Local, assignment.AssignedAtUtc.Kind);

            if (request.Status == RequestStatus.Pending)
            {
                Assert.Equal(
                    ApprovalAssignmentStatus.Pending,
                    assignment.Status);
                Assert.Null(assignment.DecidedAtUtc);
            }
            else
            {
                Assert.Equal((int)request.Status, (int)assignment.Status);
                Assert.NotNull(assignment.DecidedAtUtc);
                Assert.True(
                    assignment.DecidedAtUtc >= assignment.AssignedAtUtc);
                Assert.NotEqual(
                    DateTimeKind.Local,
                    assignment.DecidedAtUtc!.Value.Kind);
            }
        });

        AssertDepartmentUsers(
            users,
            "IT",
            employeeCount: 5,
            managerCount: 2);
        AssertDepartmentUsers(
            users,
            "HR",
            employeeCount: 4,
            managerCount: 1);
        AssertDepartmentUsers(
            users,
            "Finance",
            employeeCount: 5,
            managerCount: 2);
        AssertDepartmentUsers(
            users,
            "Marketing",
            employeeCount: 4,
            managerCount: 1);

        AssertDepartmentRequests(requests, "IT", 30, 9, 12, 6, 3);
        AssertDepartmentRequests(requests, "HR", 20, 6, 8, 4, 2);
        AssertDepartmentRequests(requests, "Finance", 30, 9, 12, 6, 3);
        AssertDepartmentRequests(requests, "Marketing", 20, 6, 8, 4, 2);
    }

    [Fact]
    public async Task SeedAsync_WhenCompleteSeedExists_IsVerifiedNoOp()
    {
        await using var database = await CreateDatabaseAsync();
        var password = CreateTestPassword();
        var seeder = new DevelopmentDataSeeder(
            database.Context,
            new PasswordHasherService());

        Assert.Equal(
            DevelopmentSeedResult.Inserted,
            await seeder.SeedAsync(password));

        Assert.Equal(
            DevelopmentSeedResult.AlreadySeeded,
            await seeder.SeedAsync(CreateTestPassword()));
        Assert.Equal(24, await database.Context.Users.CountAsync());
        Assert.Equal(100, await database.Context.ApprovalRequests.CountAsync());
        Assert.Equal(100, await database.Context.ApprovalAssignments.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_WhenPartialSeedExists_RefusesWithoutChangingIt()
    {
        await using var database = await CreateDatabaseAsync();
        var department = await database.Context.Departments
            .SingleAsync(x => x.Name == "IT");
        var existing = new User(
            "Existing seed-like account",
            "flowdesk.seed.v1.employee.it.01@dev.flowdesk.test",
            "existing-hash");
        existing.ChangeRole(UserRoles.Employee);
        database.Context.Entry(existing)
            .Property(x => x.DepartmentId)
            .CurrentValue = department.Id;
        database.Context.Users.Add(existing);
        await database.Context.SaveChangesAsync();

        var seeder = new DevelopmentDataSeeder(
            database.Context,
            new PasswordHasherService());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => seeder.SeedAsync(CreateTestPassword()));

        Assert.Equal(1, await database.Context.Users.CountAsync());
        Assert.Empty(await database.Context.ApprovalRequests.ToListAsync());
        Assert.Empty(await database.Context.ApprovalAssignments.ToListAsync());
    }

    [Fact]
    public async Task SeedAsync_WhenSeedRequestTitleExistsWithoutSeedAccounts_RefusesDuplicates()
    {
        await using var database = await CreateDatabaseAsync();
        database.Context.ApprovalRequests.Add(
            new ApprovalRequest(
                "Laptop replacement for project team (IT-001)",
                1249.99m));
        await database.Context.SaveChangesAsync();
        var seeder = new DevelopmentDataSeeder(
            database.Context,
            new PasswordHasherService());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => seeder.SeedAsync(CreateTestPassword()));

        Assert.Empty(await database.Context.Users
            .Where(x => x.Email.StartsWith("flowdesk.seed.v1."))
            .ToListAsync());
        Assert.Single(await database.Context.ApprovalRequests.ToListAsync());
        Assert.Empty(await database.Context.ApprovalAssignments.ToListAsync());
    }

    [Fact]
    public async Task SeedAsync_WhenAssignmentInsertFails_RollsBackAllSeedWrites()
    {
        await using var database = await CreateDatabaseAsync();
        await database.Context.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER RejectSeedAssignments " +
            "BEFORE INSERT ON ApprovalAssignments " +
            "BEGIN SELECT RAISE(ABORT, 'test failure'); END;");

        var seeder = new DevelopmentDataSeeder(
            database.Context,
            new PasswordHasherService());

        await Assert.ThrowsAsync<DbUpdateException>(
            () => seeder.SeedAsync(CreateTestPassword()));

        Assert.Empty(await database.Context.Users
            .Where(x => x.Email.StartsWith("flowdesk.seed.v1."))
            .ToListAsync());
        Assert.Empty(await database.Context.ApprovalRequests.ToListAsync());
        Assert.Empty(await database.Context.ApprovalAssignments.ToListAsync());
    }

    [Fact]
    public async Task SeedAsync_WhenPostSaveValidationFails_RollsBackAllSeedWrites()
    {
        await using var database = await CreateDatabaseAsync();
        await database.Context.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER CorruptSeedAssignment
            AFTER INSERT ON ApprovalAssignments
            BEGIN
                UPDATE ApprovalAssignments
                SET ApproverUserId = (
                    SELECT Id
                    FROM Users
                    WHERE Role = 'Manager'
                      AND DepartmentId != (
                          SELECT DepartmentId
                          FROM ApprovalRequests
                          WHERE Id = NEW.RequestId)
                    LIMIT 1)
                WHERE Id = NEW.Id;
            END;
            """);
        var seeder = new DevelopmentDataSeeder(
            database.Context,
            new PasswordHasherService());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => seeder.SeedAsync(CreateTestPassword()));

        Assert.Empty(await database.Context.Users
            .Where(x => x.Email.StartsWith("flowdesk.seed.v1."))
            .ToListAsync());
        Assert.Empty(await database.Context.ApprovalRequests.ToListAsync());
        Assert.Empty(await database.Context.ApprovalAssignments.ToListAsync());
    }

    [Fact]
    public async Task SeededPendingRequests_CanBeApprovedAndRejectedByTheirManager()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            context.Departments.AddRange(
                new Department("IT", "IT"),
                new Department("HR", "HR"),
                new Department("Finance", "FIN"),
                new Department("Marketing", "MKT"));
            await context.SaveChangesAsync();

            var seeder = new DevelopmentDataSeeder(
                context,
                new PasswordHasherService());
            Assert.Equal(
                DevelopmentSeedResult.Inserted,
                await seeder.SeedAsync(CreateTestPassword()));
        }

        using var reviewScope = factory.Services.CreateScope();
        var reviewContext = reviewScope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        var manager = await reviewContext.Users
            .SingleAsync(x =>
                x.Email == "flowdesk.seed.v1.manager.it.01@dev.flowdesk.test");
        var requests = await reviewContext.ApprovalRequests
            .Where(x =>
                x.Status == RequestStatus.Pending &&
                x.Assignments.Any(a => a.ApproverUserId == manager.Id))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .Take(2)
            .ToArrayAsync();
        Assert.Equal(2, requests.Length);

        using var client = factory.CreateAuthorizedClient(
            manager.Id,
            UserRoles.Manager);
        using var approve = await client.PatchAsync(
            $"/api/requests/{requests[0]}/approve",
            content: null);
        using var reject = await client.PatchAsync(
            $"/api/requests/{requests[1]}/reject",
            content: null);

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        var decisions = await reviewContext.ApprovalRequests
            .Where(x => requests.Contains(x.Id))
            .Include(x => x.Assignments)
            .ToListAsync();
        Assert.Contains(decisions, x =>
            x.Status == RequestStatus.Approved &&
            x.Assignments.Single().Status == ApprovalAssignmentStatus.Approved);
        Assert.Contains(decisions, x =>
            x.Status == RequestStatus.Rejected &&
            x.Assignments.Single().Status == ApprovalAssignmentStatus.Rejected);
    }

    [Fact]
    public void SafetyPolicy_RequiresDevelopmentAndExactLocalDatabase()
    {
        DevelopmentSeedSafetyPolicy.ValidateEnvironment("Development", null);
        DevelopmentSeedSafetyPolicy.ValidateEnvironment(
            "Development",
            "Development");
        var connection = DevelopmentSeedSafetyPolicy.ValidateConnectionString(
            "Server=localhost;Database=FlowDeskDb_New;Trusted_Connection=True;");
        Assert.Equal(
            DevelopmentSeedSafetyPolicy.RequiredDatabase,
            connection.InitialCatalog);

        Assert.Throws<InvalidOperationException>(() =>
            DevelopmentSeedSafetyPolicy.ValidateEnvironment("Production", null));
        Assert.Throws<InvalidOperationException>(() =>
            DevelopmentSeedSafetyPolicy.ValidateEnvironment(
                "Development",
                "Production"));
        Assert.Throws<InvalidOperationException>(() =>
            DevelopmentSeedSafetyPolicy.ValidateConnectionString(
                "Server=localhost;Database=FlowDesk_Production;"));
        Assert.Throws<InvalidOperationException>(() =>
            DevelopmentSeedSafetyPolicy.ValidateConnectionString(
                "Server=sql.example.com;Database=FlowDeskDb_New;"));
    }

    private static async Task<SeederDatabase> CreateDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Departments.AddRange(
            new Department("IT", "IT"),
            new Department("HR", "HR"),
            new Department("Finance", "FIN"),
            new Department("Marketing", "MKT"));
        await context.SaveChangesAsync();
        return new SeederDatabase(connection, context);
    }

    private static string CreateTestPassword()
    {
        return $"Test-{Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))}!";
    }

    private static void AssertDepartmentUsers(
        IReadOnlyCollection<User> users,
        string departmentName,
        int employeeCount,
        int managerCount)
    {
        Assert.Equal(
            employeeCount,
            users.Count(x =>
                x.Department?.Name == departmentName &&
                x.Role == UserRoles.Employee));
        Assert.Equal(
            managerCount,
            users.Count(x =>
                x.Department?.Name == departmentName &&
                x.Role == UserRoles.Manager));
    }

    private static void AssertDepartmentRequests(
        IReadOnlyCollection<ApprovalRequest> requests,
        string departmentName,
        int total,
        int pending,
        int approved,
        int rejected,
        int cancelled)
    {
        var departmentRequests = requests
            .Where(x => x.Department?.Name == departmentName)
            .ToArray();

        Assert.Equal(total, departmentRequests.Length);
        Assert.Equal(
            pending,
            departmentRequests.Count(x => x.Status == RequestStatus.Pending));
        Assert.Equal(
            approved,
            departmentRequests.Count(x => x.Status == RequestStatus.Approved));
        Assert.Equal(
            rejected,
            departmentRequests.Count(x => x.Status == RequestStatus.Rejected));
        Assert.Equal(
            cancelled,
            departmentRequests.Count(x => x.Status == RequestStatus.Cancelled));
    }

    private sealed class SeederDatabase(
        SqliteConnection connection,
        AppDbContext context) : IAsyncDisposable
    {
        public AppDbContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
