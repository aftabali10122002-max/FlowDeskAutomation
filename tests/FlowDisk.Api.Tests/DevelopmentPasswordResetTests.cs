using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using FlowDesk.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class DevelopmentPasswordResetTests
{
    [Fact]
    public void SafetyPolicy_AllowsOnlyDevelopmentAndLocalPhase4Test()
    {
        DevelopmentPasswordResetPolicy.ValidateEnvironment(
            "Development",
            null);
        DevelopmentPasswordResetPolicy.ValidateEnvironment(
            "Development",
            "Development");

        var connection = DevelopmentPasswordResetPolicy
            .ValidateConnectionString(
                "Server=localhost;Database=FlowDeskDb_Phase4Test;Trusted_Connection=True;");
        Assert.Equal(
            DevelopmentPasswordResetPolicy.RequiredDatabase,
            connection.InitialCatalog);

        Assert.Throws<InvalidOperationException>(() =>
            DevelopmentPasswordResetPolicy.ValidateEnvironment(
                "Production",
                null));
        Assert.Throws<InvalidOperationException>(() =>
            DevelopmentPasswordResetPolicy.ValidateEnvironment(
                "Development",
                "Production"));
        Assert.Throws<InvalidOperationException>(() =>
            DevelopmentPasswordResetPolicy.ValidateEnvironment(null, null));
        Assert.Throws<InvalidOperationException>(() =>
            DevelopmentPasswordResetPolicy.ValidateConnectionString(
                "Server=localhost;Database=FlowDeskDb_New;"));
        Assert.Throws<InvalidOperationException>(() =>
            DevelopmentPasswordResetPolicy.ValidateConnectionString(
                "Server=sql.example.test;Database=FlowDeskDb_Phase4Test;"));
    }

    [Fact]
    public async Task ResetAsync_ChangesOnlySelectedPasswordHashes()
    {
        await using var database = await CreateDatabaseAsync();
        var hasher = new PasswordHasherService();
        var department = new Department("IT", "IT");
        var selectedUser = new User(
            "Selected Employee",
            "selected@example.test",
            hasher.Hash("Old selected password 123!"));
        var unselectedUser = new User(
            "Unselected Manager",
            "unselected@example.test",
            hasher.Hash("Old unselected password 123!"));
        unselectedUser.ChangeRole(UserRoles.Manager);
        database.Context.Departments.Add(department);
        await database.Context.SaveChangesAsync();
        selectedUser.ChangeDepartment(department.Id);
        unselectedUser.ChangeDepartment(department.Id);
        database.Context.Users.AddRange(selectedUser, unselectedUser);
        await database.Context.SaveChangesAsync();

        var request = new ApprovalRequest(
            "Preserved request",
            75m,
            selectedUser.Id,
            department.Id);
        database.Context.ApprovalRequests.Add(request);
        await database.Context.SaveChangesAsync();
        var assignment = new ApprovalAssignment(
            request.Id,
            unselectedUser.Id);
        database.Context.ApprovalAssignments.Add(assignment);
        await database.Context.SaveChangesAsync();

        var selectedOriginalHash = selectedUser.PasswordHash;
        var unselectedOriginalHash = unselectedUser.PasswordHash;
        var resetService = new DevelopmentPasswordResetService(
            database.Context,
            hasher);

        var resetCount = await resetService.ResetAsync(
            [selectedUser.Email],
            "New selected development password 123!");

        Assert.Equal(1, resetCount);
        var persistedUsers = await database.Context.Users
            .AsNoTracking()
            .ToDictionaryAsync(user => user.Email);
        var selectedAfter = persistedUsers[selectedUser.Email];
        var unselectedAfter = persistedUsers[unselectedUser.Email];
        Assert.NotEqual(selectedOriginalHash, selectedAfter.PasswordHash);
        Assert.True(hasher.Verify(
            "New selected development password 123!",
            selectedAfter.PasswordHash));
        Assert.False(hasher.Verify(
            "Old selected password 123!",
            selectedAfter.PasswordHash));
        Assert.Equal(unselectedOriginalHash, unselectedAfter.PasswordHash);
        Assert.True(hasher.Verify(
            "Old unselected password 123!",
            unselectedAfter.PasswordHash));
        Assert.Equal(UserRoles.Employee, selectedAfter.Role);
        Assert.Equal(UserRoles.Manager, unselectedAfter.Role);
        Assert.Equal(department.Id, selectedAfter.DepartmentId);
        Assert.Equal(department.Id, unselectedAfter.DepartmentId);
        Assert.Equal(1, await database.Context.ApprovalRequests.CountAsync());
        Assert.Equal(1, await database.Context.ApprovalAssignments.CountAsync());
        Assert.Equal(
            "Preserved request",
            (await database.Context.ApprovalRequests
                .AsNoTracking()
                .SingleAsync()).Title);
        Assert.Equal(
            unselectedUser.Id,
            (await database.Context.ApprovalAssignments
                .AsNoTracking()
                .SingleAsync()).ApproverUserId);
    }

    [Fact]
    public async Task ResetAsync_WhenAnySelectedAccountIsMissing_ChangesNoHashes()
    {
        await using var database = await CreateDatabaseAsync();
        var hasher = new PasswordHasherService();
        var user = new User(
            "Existing Employee",
            "existing@example.test",
            hasher.Hash("Old existing password 123!"));
        database.Context.Users.Add(user);
        await database.Context.SaveChangesAsync();
        var originalHash = user.PasswordHash;
        var resetService = new DevelopmentPasswordResetService(
            database.Context,
            hasher);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resetService.ResetAsync(
                [user.Email, "missing@example.test"],
                "New selected development password 123!"));

        database.Context.ChangeTracker.Clear();
        var persistedUser = await database.Context.Users.SingleAsync();
        Assert.Equal(originalHash, persistedUser.PasswordHash);
        Assert.True(hasher.Verify(
            "Old existing password 123!",
            persistedUser.PasswordHash));
    }

    [Fact]
    public async Task ResetAsync_RejectsEmptyDuplicateAndShortSelections()
    {
        await using var database = await CreateDatabaseAsync();
        var resetService = new DevelopmentPasswordResetService(
            database.Context,
            new PasswordHasherService());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            resetService.ResetAsync(
                [],
                "New selected development password 123!"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            resetService.ResetAsync(
                ["account@example.test", "ACCOUNT@example.test"],
                "New selected development password 123!"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            resetService.ResetAsync(
                ["account@example.test"],
                "short"));
    }

    private static async Task<ResetDatabase> CreateDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return new ResetDatabase(connection, context);
    }

    private sealed class ResetDatabase(
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
