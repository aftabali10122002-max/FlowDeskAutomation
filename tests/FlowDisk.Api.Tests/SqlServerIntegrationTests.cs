using System.Net;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FlowDisk.Api.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    private const string ConnectionStringEnvironmentVariable =
        "FLOWDESK_SQLSERVER_TEST_CONNECTION_STRING";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                ConnectionStringEnvironmentVariable)))
        {
            Skip = $"Set {ConnectionStringEnvironmentVariable} to run " +
                "against a separately provisioned SQL Server test database.";
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerIntegrationCollection
{
    public const string Name = "SQL Server integration";
}

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SqlServerIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable =
        "FLOWDESK_SQLSERVER_TEST_CONNECTION_STRING";

    [SqlServerFact]
    public async Task MigrationsApplyAndRoleAndStatusConstraintsRejectInvalidValues()
    {
        await using var factory = CreateFactory();
        await factory.InitializeDatabaseAsync();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await AssertConstraintRejectsAsync(
            context,
            "INSERT INTO dbo.Users " +
            "(FullName, Email, PasswordHash, Role, CreatedAtUtc) " +
            "VALUES ({0}, {1}, {2}, {3}, SYSUTCDATETIME())",
            "Constraint test",
            $"{Guid.NewGuid():N}@example.test",
            "test-hash-not-a-credential",
            "SuperAdmin");

        await AssertConstraintRejectsAsync(
            context,
            "INSERT INTO dbo.ApprovalRequests " +
            "(Title, Amount, Status, CreatedAtUtc) " +
            "VALUES ({0}, {1}, {2}, SYSUTCDATETIME())",
            $"Invalid status {Guid.NewGuid():N}",
            1m,
            99);
    }

    [SqlServerFact]
    public async Task SqlServerAuthorizationKeepsManagerScopeAndAdminAccess()
    {
        await using var factory = CreateFactory();
        await factory.InitializeDatabaseAsync();
        var (owner, manager, otherManager, admin, request) =
            await factory.SeedAssignedRequestAsync();
        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            UserRoles.Manager);
        using var otherManagerClient = factory.CreateAuthorizedClient(
            otherManager.Id,
            UserRoles.Manager);
        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);
        using var employeeClient = factory.CreateAuthorizedClient(
            owner.Id,
            UserRoles.Employee);

        using var managerRead = await managerClient.GetAsync(
            $"/api/requests/{request.Id}");
        using var otherManagerRead = await otherManagerClient.GetAsync(
            $"/api/requests/{request.Id}");
        using var adminRead = await adminClient.GetAsync(
            "/api/admin/users");
        using var employeeAdminRead = await employeeClient.GetAsync(
            "/api/admin/users");
        using var approve = await managerClient.PatchAsync(
            $"/api/requests/{request.Id}/approve",
            content: null);

        Assert.Equal(HttpStatusCode.OK, managerRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherManagerRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeAdminRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await context.ApprovalRequests
            .SingleAsync(x => x.Id == request.Id);
        Assert.Equal(RequestStatus.Approved, persisted.Status);
    }

    private static SqlServerIntegrationFactory CreateFactory()
    {
        ApiFactory.ConfigureTestEnvironment();
        var connectionString = Environment.GetEnvironmentVariable(
            ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringEnvironmentVariable} is required.");
        }

        var settings = new SqlConnectionStringBuilder(connectionString);
        if (!settings.InitialCatalog.StartsWith(
                "FlowDesk_Test_",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                settings.InitialCatalog,
                "FlowDeskDb_New",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SQL Server integration tests require a separately provisioned " +
                "database whose name starts with FlowDesk_Test_.");
        }

        return new SqlServerIntegrationFactory(connectionString);
    }

    private static async Task AssertConstraintRejectsAsync(
        AppDbContext context,
        string sql,
        params object[] parameters)
    {
        await using var transaction =
            await context.Database.BeginTransactionAsync();
        try
        {
            await Assert.ThrowsAsync<SqlException>(async () =>
            {
                await context.Database.ExecuteSqlRawAsync(sql, parameters);
            });
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private sealed class SqlServerIntegrationFactory(string connectionString)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["Jwt:SecretKey"] = ApiFactory.TestSigningKey,
                    ["Jwt:Issuer"] = ApiFactory.Issuer,
                    ["Jwt:Audience"] = ApiFactory.Audience,
                    ["Jwt:ExpirationMinutes"] = "30",
                    ["RateLimiting:Login:PermitLimit"] = "10",
                    ["RateLimiting:Registration:PermitLimit"] = "5"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<
                    IDbContextOptionsConfiguration<AppDbContext>>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlServer(connectionString));
            });
        }

        public async Task InitializeDatabaseAsync()
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await context.Database.CanConnectAsync())
            {
                throw new InvalidOperationException(
                    "The separately provisioned SQL Server test database " +
                    "must already exist and be reachable.");
            }

            var appliedMigrations =
                await context.Database.GetAppliedMigrationsAsync();
            if (!appliedMigrations.Any())
            {
                var connection = context.Database.GetDbConnection();
                await connection.OpenAsync();
                int existingTableCount;
                try
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText =
                        "SELECT COUNT(*) FROM sys.tables " +
                        "WHERE schema_id = SCHEMA_ID(N'dbo') " +
                        "AND name <> N'__EFMigrationsHistory'";
                    existingTableCount = Convert.ToInt32(
                        await command.ExecuteScalarAsync());
                }
                finally
                {
                    await connection.CloseAsync();
                }

                if (existingTableCount != 0)
                {
                    throw new InvalidOperationException(
                        "An unmigrated SQL Server test database must not " +
                        "contain application tables.");
                }
            }

            await context.Database.MigrateAsync();
        }

        public HttpClient CreateAuthorizedClient(int userId, string role)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    ApiFactory.CreateToken(userId, role));
            return client;
        }

        public async Task<(User Owner, User Manager, User OtherManager,
            User Admin, ApprovalRequest Request)> SeedAssignedRequestAsync()
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = CreateUser(UserRoles.Employee);
            var manager = CreateUser(UserRoles.Manager);
            var otherManager = CreateUser(UserRoles.Manager);
            var admin = CreateUser(UserRoles.Admin);
            context.Users.AddRange(owner, manager, otherManager, admin);
            await context.SaveChangesAsync();

            var request = new ApprovalRequest(
                $"SQL integration request {Guid.NewGuid():N}",
                25m,
                owner.Id,
                departmentId: null);
            context.ApprovalRequests.Add(request);
            await context.SaveChangesAsync();
            context.ApprovalAssignments.Add(
                new ApprovalAssignment(request.Id, manager.Id));
            await context.SaveChangesAsync();

            return (owner, manager, otherManager, admin, request);
        }

        private static User CreateUser(string role)
        {
            var unique = Guid.NewGuid().ToString("N");
            var user = new User(
                $"SQL integration {unique}",
                $"{unique}@example.test",
                "test-hash-not-a-credential");
            user.ChangeRole(role);
            return user;
        }
    }
}
