using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowDesk.Domain.Entities;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowDisk.Api.Tests;

public class AdminManagementTests
{
    [Fact]
    public async Task Admin_CanListReadAndChangeUserRole_WithoutSensitiveFields()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync(UserRoles.Admin);
        var employee = await factory.AddUserAsync();
        using var client = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);

        using var listResponse = await client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", listJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", listJson, StringComparison.OrdinalIgnoreCase);
        using var listDocument = JsonDocument.Parse(listJson);
        Assert.Contains(
            listDocument.RootElement.EnumerateArray(),
            item => item.GetProperty("id").GetInt32() == employee.Id);

        using var getResponse = await client.GetAsync(
            $"/api/admin/users/{employee.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        using var updateResponse = await client.PatchAsJsonAsync(
            $"/api/admin/users/{employee.Id}/role",
            new { role = UserRoles.Manager });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updateDocument = JsonDocument.Parse(
            await updateResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            UserRoles.Manager,
            updateDocument.RootElement.GetProperty("role").GetString());
        Assert.DoesNotContain(
            "passwordHash",
            updateDocument.RootElement.GetRawText(),
            StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedUser = await context.Users
            .SingleAsync(user => user.Id == employee.Id);
        Assert.Equal(UserRoles.Manager, persistedUser.Role);
    }

    [Fact]
    public async Task Admin_CanListAndCreateDepartments()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync(UserRoles.Admin);
        using var client = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);

        using var createResponse = await client.PostAsJsonAsync(
            "/api/admin/departments",
            new { name = "  Finance  ", code = " fin " });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createJson = await createResponse.Content.ReadAsStringAsync();
        using var createDocument = JsonDocument.Parse(createJson);
        var departmentId = createDocument.RootElement
            .GetProperty("id")
            .GetInt32();
        Assert.Equal(
            "Finance",
            createDocument.RootElement.GetProperty("name").GetString());
        Assert.Equal(
            "FIN",
            createDocument.RootElement.GetProperty("code").GetString());

        using var duplicateResponse = await client.PostAsJsonAsync(
            "/api/admin/departments",
            new { name = "Finance", code = "OTHER" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        using var listResponse = await client.GetAsync(
            "/api/admin/departments");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listDocument = JsonDocument.Parse(
            await listResponse.Content.ReadAsStringAsync());
        Assert.Contains(
            listDocument.RootElement.EnumerateArray(),
            item => item.GetProperty("id").GetInt32() == departmentId);
    }

    [Fact]
    public async Task Admin_CanCreateEmployeeOrManager_AndManageDepartmentWithoutExposingPassword()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync(UserRoles.Admin);
        using var client = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);

        using var departmentResponse = await client.PostAsJsonAsync(
            "/api/admin/departments",
            new { name = "Operations", code = "OPS" });
        Assert.Equal(HttpStatusCode.Created, departmentResponse.StatusCode);
        using var departmentDocument = JsonDocument.Parse(
            await departmentResponse.Content.ReadAsStringAsync());
        var departmentId = departmentDocument.RootElement
            .GetProperty("id")
            .GetInt32();

        const string password = "DevelopmentOnlyPassword123!";
        var email = $"{Guid.NewGuid():N}@example.test";
        using var employeeResponse = await client.PostAsJsonAsync(
            "/api/admin/users",
            new
            {
                fullName = "  Created Employee  ",
                email,
                password,
                role = UserRoles.Employee,
                departmentId
            });
        Assert.Equal(HttpStatusCode.Created, employeeResponse.StatusCode);
        var employeeJson = await employeeResponse.Content.ReadAsStringAsync();
        using var employeeDocument = JsonDocument.Parse(employeeJson);
        var employeeId = employeeDocument.RootElement
            .GetProperty("id")
            .GetInt32();
        Assert.Equal("Created Employee", employeeDocument.RootElement
            .GetProperty("fullName").GetString());
        Assert.Equal(departmentId, employeeDocument.RootElement
            .GetProperty("departmentId").GetInt32());
        Assert.DoesNotContain(password, employeeJson, StringComparison.Ordinal);

        using var duplicateEmail = await client.PostAsJsonAsync(
            "/api/admin/users",
            new
            {
                fullName = "Duplicate",
                email,
                password,
                role = UserRoles.Manager,
                departmentId
            });
        using var adminCreation = await client.PostAsJsonAsync(
            "/api/admin/users",
            new
            {
                fullName = "Not permitted",
                email = $"{Guid.NewGuid():N}@example.test",
                password,
                role = UserRoles.Admin,
                departmentId
            });
        Assert.Equal(HttpStatusCode.Conflict, duplicateEmail.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, adminCreation.StatusCode);

        using var updateDepartment = await client.PatchAsJsonAsync(
            $"/api/admin/users/{employeeId}/department",
            new { departmentId = (int?)null });
        Assert.Equal(HttpStatusCode.OK, updateDepartment.StatusCode);
        using var invalidDepartment = await client.PatchAsJsonAsync(
            $"/api/admin/users/{employeeId}/department",
            new { departmentId = 999999 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidDepartment.StatusCode);

        using var managerResponse = await client.PostAsJsonAsync(
            "/api/admin/users",
            new
            {
                fullName = "Created Manager",
                email = $"{Guid.NewGuid():N}@example.test",
                password,
                role = UserRoles.Manager,
                departmentId
            });
        Assert.Equal(HttpStatusCode.Created, managerResponse.StatusCode);
        using var managerDocument = JsonDocument.Parse(
            await managerResponse.Content.ReadAsStringAsync());
        Assert.Equal(UserRoles.Manager, managerDocument.RootElement
            .GetProperty("role").GetString());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var createdEmployee = await context.Users.SingleAsync(
            user => user.Id == employeeId);
        Assert.NotEqual(password, createdEmployee.PasswordHash);
    }

    [Fact]
    public async Task Admin_CannotChangeTheirOwnRole()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync(UserRoles.Admin);
        using var client = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);

        using var response = await client.PatchAsJsonAsync(
            $"/api/admin/users/{admin.Id}/role",
            new { role = UserRoles.Employee });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedAdmin = await context.Users.SingleAsync(
            user => user.Id == admin.Id);
        Assert.Equal(UserRoles.Admin, persistedAdmin.Role);
    }

    [Fact]
    public async Task Admin_CannotDeactivateManagerWithOpenWork_ButCanAfterItIsResolved()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync(UserRoles.Admin);
        var employee = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync(UserRoles.Manager);
        var request = await factory.AddRequestAsync(employee.Id);
        await factory.AddAssignmentAsync(request.Id, manager.Id);
        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);
        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            UserRoles.Manager);

        using var blockedDeactivation = await adminClient.PatchAsJsonAsync(
            $"/api/admin/users/{manager.Id}/active",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, blockedDeactivation.StatusCode);

        using var approval = await managerClient.PatchAsync(
            $"/api/requests/{request.Id}/approve",
            content: null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);

        using var deactivation = await adminClient.PatchAsJsonAsync(
            $"/api/admin/users/{manager.Id}/active",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivation.StatusCode);
        using var staleManagerRequest = await managerClient.GetAsync(
            "/api/requests");
        Assert.Equal(HttpStatusCode.Forbidden, staleManagerRequest.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Employee)]
    [InlineData(UserRoles.Manager)]
    public async Task NonAdminRoles_CannotAccessAdminManagementEndpoints(
        string role)
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.AddUserAsync(role);
        using var client = factory.CreateAuthorizedClient(user.Id, role);

        using var listUsers = await client.GetAsync("/api/admin/users");
        using var getUser = await client.GetAsync("/api/admin/users/1");
        using var updateRole = await client.PatchAsJsonAsync(
            "/api/admin/users/1/role",
            new { role = UserRoles.Admin });
        using var listDepartments = await client.GetAsync(
            "/api/admin/departments");
        using var listWorkflows = await client.GetAsync(
            "/api/admin/workflows");
        using var createDepartment = await client.PostAsJsonAsync(
            "/api/admin/departments",
            new { name = "Restricted" });
        using var createUser = await client.PostAsJsonAsync(
            "/api/admin/users",
            new
            {
                fullName = "Restricted",
                email = $"{Guid.NewGuid():N}@example.test",
                password = "LongEnoughTestPassword123!",
                role = UserRoles.Employee,
                departmentId = 1
            });
        using var updateDepartment = await client.PatchAsJsonAsync(
            "/api/admin/users/1/department",
            new { departmentId = 2 });

        Assert.Equal(HttpStatusCode.Forbidden, listUsers.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, getUser.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, updateRole.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, listDepartments.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, listWorkflows.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createDepartment.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createUser.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, updateDepartment.StatusCode);
    }

    [Fact]
    public async Task UserManagement_RejectsInvalidInputAndMissingUsers()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync(UserRoles.Admin);
        using var client = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);

        using var invalidId = await client.GetAsync("/api/admin/users/0");
        using var invalidRole = await client.PatchAsJsonAsync(
            "/api/admin/users/1/role",
            new { role = "SuperAdmin" });
        using var missingUser = await client.GetAsync(
            "/api/admin/users/999999");
        using var missingUserUpdate = await client.PatchAsJsonAsync(
            "/api/admin/users/999999/role",
            new { role = UserRoles.Manager });

        Assert.Equal(HttpStatusCode.BadRequest, invalidId.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRole.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingUser.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingUserUpdate.StatusCode);
    }

    [Fact]
    public async Task AdminRoleClaim_IsInsufficientWhenPersistedRoleIsNotAdmin()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var manager = await factory.AddUserAsync(UserRoles.Manager);
        using var client = factory.CreateAuthorizedClient(
            manager.Id,
            UserRoles.Admin);

        using var response = await client.GetAsync("/api/admin/users");
        using var workflows = await client.GetAsync("/api/admin/workflows");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, workflows.StatusCode);
    }

    [Fact]
    public async Task DepartmentCreation_RejectsInvalidNamesAndCodes()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync(UserRoles.Admin);
        using var client = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);

        using var emptyName = await client.PostAsJsonAsync(
            "/api/admin/departments",
            new { name = "  ", code = "FIN" });
        using var longName = await client.PostAsJsonAsync(
            "/api/admin/departments",
            new { name = new string('N', 101), code = "FIN" });
        using var longCode = await client.PostAsJsonAsync(
            "/api/admin/departments",
            new { name = "Finance", code = new string('C', 21) });

        Assert.Equal(HttpStatusCode.BadRequest, emptyName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longCode.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Admin)]
    [InlineData(UserRoles.Manager)]
    public async Task PublicRegistration_AlwaysCreatesEmployee(string role)
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                fullName = "Public Registration",
                email = $"{Guid.NewGuid():N}@example.test",
                password = "LongEnoughTestPassword123!",
                role
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.Equal(
            UserRoles.Employee,
            document.RootElement.GetProperty("role").GetString());
        Assert.DoesNotContain(
            "passwordHash",
            document.RootElement.GetRawText(),
            StringComparison.OrdinalIgnoreCase);
    }
}
