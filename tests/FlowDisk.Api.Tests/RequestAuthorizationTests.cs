using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowDisk.Api.Tests;

public class RequestAuthorizationTests
{
    [Fact]
    public async Task RequestEndpoints_RequireAuthentication()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var requests = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, "/api/requests", null),
            (HttpMethod.Get, "/api/requests/1", null),
            (HttpMethod.Get, "/api/requests/legacy", null),
            (HttpMethod.Get, "/api/requests/legacy/1", null),
            (HttpMethod.Post, "/api/requests",
                new { title = "Unauthorized", amount = 1m }),
            (HttpMethod.Post, "/api/requests/1/assignments",
                new { approverUserId = 2 }),
            (HttpMethod.Patch, "/api/requests/1/approve", null),
            (HttpMethod.Patch, "/api/requests/1/reject", null),
            (HttpMethod.Patch, "/api/requests/1/cancel", null)
        };

        foreach (var (method, path, body) in requests)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body != null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Registration_CreatesEmployee_AndRequestOwnerComesFromJwt()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                fullName = "Registered Employee",
                email = $"{Guid.NewGuid():N}@example.test",
                password = "LongEnoughTestPassword123!",
                role = "Admin"
            });

        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var registrationJson = await registration.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PasswordHash", registrationJson);
        Assert.DoesNotContain("Password", registrationJson);

        using var registeredDocument = JsonDocument.Parse(registrationJson);
        var registeredUserId = registeredDocument.RootElement
            .GetProperty("id")
            .GetInt32();
        Assert.Equal("Employee", registeredDocument.RootElement
            .GetProperty("role")
            .GetString());

        using var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                email = registeredDocument.RootElement
                    .GetProperty("email")
                    .GetString(),
                password = "LongEnoughTestPassword123!"
            });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginJson = await login.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PasswordHash", loginJson);
        using var loginDocument = JsonDocument.Parse(loginJson);
        var token = loginDocument.RootElement
            .GetProperty("accessToken")
            .GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var claims = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(token)
            .Claims;
        Assert.Contains(claims, claim =>
            (claim.Type == "role" || claim.Type == ClaimTypes.Role) &&
            claim.Value == "Employee");

        using var requestClient = factory.CreateAuthorizedClient(
            registeredUserId,
            "Employee");
        using var created = await requestClient.PostAsJsonAsync(
            "/api/requests",
            new
            {
                title = "Owned by token identity",
                amount = 15m,
                createdByUserId = registeredUserId + 1000,
                approverUserId = registeredUserId + 2000
            });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var requestRow = await context.ApprovalRequests
            .SingleAsync(x => x.Title == "Owned by token identity");
        Assert.Equal(registeredUserId, requestRow.CreatedByUserId);
        Assert.Null(requestRow.DepartmentId);
    }

    [Fact]
    public async Task Employee_CannotReadOrCancelAnotherEmployeesRequest()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await factory.AddUserAsync();
        var otherEmployee = await factory.AddUserAsync();
        var request = await factory.AddRequestAsync(owner.Id);
        using var client = factory.CreateAuthorizedClient(
            otherEmployee.Id,
            "Employee");

        var list = await client.GetFromJsonAsync<List<JsonElement>>(
            "/api/requests");
        Assert.Empty(list!);

        using var get = await client.GetAsync($"/api/requests/{request.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using var cancel = await client.PatchAsync(
            $"/api/requests/{request.Id}/cancel",
            content: null);
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await context.ApprovalRequests
            .SingleAsync(x => x.Id == request.Id);
        Assert.Equal(RequestStatus.Pending, persisted.Status);
        Assert.Equal(owner.Id, persisted.CreatedByUserId);
    }

    [Fact]
    public async Task Employee_CannotApproveOrRejectRequests()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var employee = await factory.AddUserAsync();
        var request = await factory.AddRequestAsync(employee.Id);
        using var client = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");

        using var approve = await client.PatchAsync(
            $"/api/requests/{request.Id}/approve",
            content: null);
        using var reject = await client.PatchAsync(
            $"/api/requests/{request.Id}/reject",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reject.StatusCode);
    }

    [Fact]
    public async Task Employee_CanCancelOwnPendingRequest_AndClosesAssignment()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync("Manager");
        var request = await factory.AddRequestAsync(owner.Id);
        await factory.AddAssignmentAsync(request.Id, manager.Id);
        using var client = factory.CreateAuthorizedClient(owner.Id, "Employee");

        using var cancel = await client.PatchAsync(
            $"/api/requests/{request.Id}/cancel",
            content: null);

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedRequest = await context.ApprovalRequests
            .SingleAsync(x => x.Id == request.Id);
        var assignment = await context.ApprovalAssignments
            .SingleAsync(x => x.RequestId == request.Id);
        Assert.Equal(RequestStatus.Cancelled, persistedRequest.Status);
        Assert.Equal(ApprovalAssignmentStatus.Cancelled, assignment.Status);
        Assert.NotNull(assignment.DecidedAtUtc);
    }

    [Fact]
    public async Task Manager_CannotReadOrDecideUnassignedOrOtherManagersRequest()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync("Manager");
        var otherManager = await factory.AddUserAsync("Manager");
        var request = await factory.AddRequestAsync(owner.Id);
        await factory.AddAssignmentAsync(request.Id, otherManager.Id);
        var completedRequest = await factory.AddRequestAsync(owner.Id);
        await factory.AddAssignmentAsync(completedRequest.Id, otherManager.Id);
        using (var otherManagerClient = factory.CreateAuthorizedClient(
                   otherManager.Id,
                   "Manager"))
        using (var otherManagerApproval = await otherManagerClient.PatchAsync(
                   $"/api/requests/{completedRequest.Id}/approve",
                   content: null))
        {
            Assert.Equal(HttpStatusCode.OK, otherManagerApproval.StatusCode);
        }

        var legacyRequest = await factory.AddRequestAsync(ownerUserId: null);
        await factory.AddAssignmentAsync(legacyRequest.Id, manager.Id);
        using var client = factory.CreateAuthorizedClient(
            manager.Id,
            "Manager");

        var list = await client.GetFromJsonAsync<List<JsonElement>>(
            "/api/requests");
        Assert.Empty(list!);

        using var get = await client.GetAsync($"/api/requests/{request.Id}");
        using var completedGet = await client.GetAsync(
            $"/api/requests/{completedRequest.Id}");
        using var approve = await client.PatchAsync(
            $"/api/requests/{request.Id}/approve",
            content: null);
        using var reject = await client.PatchAsync(
            $"/api/requests/{request.Id}/reject",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, completedGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reject.StatusCode);
    }

    [Fact]
    public async Task Manager_CanReadAssignedRequestHistoryAcrossAllStatuses()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync("Manager");
        var pendingRequest = await factory.AddRequestAsync(owner.Id);
        var approvedRequest = await factory.AddRequestAsync(owner.Id);
        var rejectedRequest = await factory.AddRequestAsync(owner.Id);
        var cancelledRequest = await factory.AddRequestAsync(owner.Id);

        await factory.AddAssignmentAsync(pendingRequest.Id, manager.Id);
        await factory.AddAssignmentAsync(approvedRequest.Id, manager.Id);
        await factory.AddAssignmentAsync(rejectedRequest.Id, manager.Id);
        await factory.AddAssignmentAsync(cancelledRequest.Id, manager.Id);

        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            "Manager");
        using var ownerClient = factory.CreateAuthorizedClient(
            owner.Id,
            "Employee");

        using var approve = await managerClient.PatchAsync(
            $"/api/requests/{approvedRequest.Id}/approve",
            content: null);
        using var reject = await managerClient.PatchAsync(
            $"/api/requests/{rejectedRequest.Id}/reject",
            content: null);
        using var cancel = await ownerClient.PatchAsync(
            $"/api/requests/{cancelledRequest.Id}/cancel",
            content: null);

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var requests = await managerClient.GetFromJsonAsync<List<JsonElement>>(
            "/api/requests");
        var visibleRequests = requests
            ?? throw new InvalidOperationException("Manager request list was null.");
        Assert.Equal(4, visibleRequests.Count);

        var expectedStatuses = new Dictionary<int, string>
        {
            [pendingRequest.Id] = "Pending",
            [approvedRequest.Id] = "Approved",
            [rejectedRequest.Id] = "Rejected",
            [cancelledRequest.Id] = "Cancelled"
        };

        foreach (var item in visibleRequests)
        {
            var id = item.GetProperty("id").GetInt32();
            Assert.Equal(expectedStatuses[id], item.GetProperty("status").GetString());
        }

        foreach (var (requestId, expectedStatus) in expectedStatuses)
        {
            using var response = await managerClient.GetAsync(
                $"/api/requests/{requestId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            Assert.Equal(
                expectedStatus,
                document.RootElement.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task ManagerRequestResponses_IncludeSubmitterDepartmentAndOwnAssignmentHistory()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync("Manager");

        int departmentId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var department = new FlowDesk.Domain.Entities.Department(
                "Finance",
                "FIN");
            context.Departments.Add(department);
            await context.SaveChangesAsync();
            departmentId = department.Id;
        }

        var request = await factory.AddRequestAsync(owner.Id, departmentId);
        await factory.AddAssignmentAsync(request.Id, manager.Id);
        using var client = factory.CreateAuthorizedClient(manager.Id, "Manager");

        var visibleRequests = await client.GetFromJsonAsync<List<JsonElement>>(
            "/api/requests");
        var listItem = Assert.Single(visibleRequests!);
        Assert.Equal(
            owner.FullName,
            listItem.GetProperty("submittedBy").GetString());
        Assert.Equal(
            "Finance",
            listItem.GetProperty("department").GetString());
        var listAssignment = Assert.Single(
            listItem.GetProperty("assignments").EnumerateArray());
        Assert.Equal(
            manager.FullName,
            listAssignment.GetProperty("approverName").GetString());
        Assert.Equal(
            "Pending",
            listAssignment.GetProperty("status").GetString());

        using var detailResponse = await client.GetAsync(
            $"/api/requests/{request.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detailDocument = JsonDocument.Parse(
            await detailResponse.Content.ReadAsStringAsync());
        var detail = detailDocument.RootElement;
        Assert.Equal(
            owner.FullName,
            detail.GetProperty("submittedBy").GetString());
        Assert.Equal(
            "Finance",
            detail.GetProperty("department").GetString());
        Assert.Single(detail.GetProperty("assignments").EnumerateArray());
    }

    [Fact]
    public async Task AssignedManager_CanDecideOnce_AndDecisionIsAtomic()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync("Manager");
        var request = await factory.AddRequestAsync(owner.Id);
        await factory.AddAssignmentAsync(request.Id, manager.Id);
        using var client = factory.CreateAuthorizedClient(
            manager.Id,
            "Manager");

        var assignedRequests = await client.GetFromJsonAsync<List<JsonElement>>(
            "/api/requests");
        Assert.Single(assignedRequests!);
        using var assignedRequest = await client.GetAsync(
            $"/api/requests/{request.Id}");

        using var approve = await client.PatchAsync(
            $"/api/requests/{request.Id}/approve",
            content: null);
        using var repeat = await client.PatchAsync(
            $"/api/requests/{request.Id}/approve",
            content: null);

        Assert.Equal(HttpStatusCode.OK, assignedRequest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedRequest = await context.ApprovalRequests
            .SingleAsync(x => x.Id == request.Id);
        var assignment = await context.ApprovalAssignments
            .SingleAsync(x => x.RequestId == request.Id);
        Assert.Equal(RequestStatus.Approved, persistedRequest.Status);
        Assert.Equal(ApprovalAssignmentStatus.Approved, assignment.Status);
        Assert.NotNull(assignment.DecidedAtUtc);
    }

    [Fact]
    public async Task AssignedManager_CanRejectAssignedPendingRequest()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync("Manager");
        var request = await factory.AddRequestAsync(owner.Id);
        await factory.AddAssignmentAsync(request.Id, manager.Id);
        using var client = factory.CreateAuthorizedClient(
            manager.Id,
            "Manager");

        using var reject = await client.PatchAsync(
            $"/api/requests/{request.Id}/reject",
            content: null);

        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedRequest = await context.ApprovalRequests
            .SingleAsync(x => x.Id == request.Id);
        var assignment = await context.ApprovalAssignments
            .SingleAsync(x => x.RequestId == request.Id);
        Assert.Equal(RequestStatus.Rejected, persistedRequest.Status);
        Assert.Equal(ApprovalAssignmentStatus.Rejected, assignment.Status);
        Assert.NotNull(assignment.DecidedAtUtc);
    }

    [Fact]
    public async Task Admin_CanAssignOnlyManagers_AndCannotApprove()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var department = new FlowDesk.Domain.Entities.Department(
            "Assignment department",
            "ASG");
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Departments.Add(department);
            await context.SaveChangesAsync();
        }
        var manager = await factory.AddUserAsync("Manager");
        var employee = await factory.AddUserAsync();
        var owner = await factory.AddUserAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var persistedManager = await context.Users.SingleAsync(
                user => user.Id == manager.Id);
            var persistedOwner = await context.Users.SingleAsync(
                user => user.Id == owner.Id);
            persistedManager.ChangeDepartment(department.Id);
            persistedOwner.ChangeDepartment(department.Id);
            await context.SaveChangesAsync();
        }
        var request = await factory.AddRequestAsync(owner.Id, department.Id);
        using var client = factory.CreateAuthorizedClient(admin.Id, "Admin");

        using var assignment = await client.PostAsJsonAsync(
            $"/api/requests/{request.Id}/assignments",
            new { approverUserId = manager.Id });
        var adminRequests = await client.GetFromJsonAsync<List<JsonElement>>(
            "/api/requests");
        using var duplicate = await client.PostAsJsonAsync(
            $"/api/requests/{request.Id}/assignments",
            new { approverUserId = manager.Id });
        using var invalidAssignee = await client.PostAsJsonAsync(
            $"/api/requests/{request.Id}/assignments",
            new { approverUserId = employee.Id });
        var legacyRequest = await factory.AddRequestAsync(ownerUserId: null);
        using var legacyAssignment = await client.PostAsJsonAsync(
            $"/api/requests/{legacyRequest.Id}/assignments",
            new { approverUserId = manager.Id });
        using var approve = await client.PatchAsync(
            $"/api/requests/{request.Id}/approve",
            content: null);
        using var reject = await client.PatchAsync(
            $"/api/requests/{request.Id}/reject",
            content: null);

        Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        var adminRequest = Assert.Single(adminRequests!);
        var adminAssignment = Assert.Single(
            adminRequest.GetProperty("assignments").EnumerateArray());
        Assert.Equal(
            manager.FullName,
            adminAssignment.GetProperty("approverName").GetString());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidAssignee.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, legacyAssignment.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reject.StatusCode);
    }

    [Fact]
    public async Task Admin_ReadAllAndLegacyReviewAreSeparateReadOnlyPolicies()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var owner = await factory.AddUserAsync();
        var ownedRequest = await factory.AddRequestAsync(owner.Id);
        var legacyRequest = await factory.AddRequestAsync(ownerUserId: null);
        using var client = factory.CreateAuthorizedClient(admin.Id, "Admin");

        var normalRequests = await client.GetFromJsonAsync<List<JsonElement>>(
            "/api/requests");
        var legacyRequests = await client.GetFromJsonAsync<List<JsonElement>>(
            "/api/requests/legacy");

        var visibleRequests = normalRequests
            ?? throw new InvalidOperationException("Admin request list was null.");
        var visibleLegacyRequests = legacyRequests
            ?? throw new InvalidOperationException("Legacy request list was null.");

        Assert.Equal(2, visibleRequests.Count);
        Assert.Contains(visibleRequests, item =>
            item.GetProperty("id").GetInt32() == ownedRequest.Id);
        Assert.Contains(visibleRequests, item =>
            item.GetProperty("id").GetInt32() == legacyRequest.Id);
        Assert.Single(visibleLegacyRequests);
        Assert.Equal(
            legacyRequest.Id,
            visibleLegacyRequests[0].GetProperty("id").GetInt32());

        using var legacyInNormalRoute = await client.GetAsync(
            $"/api/requests/{legacyRequest.Id}");
        using var legacyRead = await client.GetAsync(
            $"/api/requests/legacy/{legacyRequest.Id}");
        using var cancel = await client.PatchAsync(
            $"/api/requests/{ownedRequest.Id}/cancel",
            content: null);

        Assert.Equal(HttpStatusCode.OK, legacyInNormalRoute.StatusCode);
        Assert.Equal(HttpStatusCode.OK, legacyRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cancel.StatusCode);

        using var employeeClient = factory.CreateAuthorizedClient(
            owner.Id,
            "Employee");
        using var employeeLegacyList = await employeeClient.GetAsync(
            "/api/requests/legacy");
        Assert.Equal(HttpStatusCode.Forbidden, employeeLegacyList.StatusCode);
    }

    [Fact]
    public async Task DemotedAdmin_CannotUseStaleToken_AndRejectedRequestHasNoSideEffect()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var demotedAdmin = await factory.AddUserAsync("Admin");
        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            "Admin");

        using var demote = await adminClient.PatchAsJsonAsync(
            $"/api/admin/users/{demotedAdmin.Id}/role",
            new { role = "Employee" });
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);

        const string departmentName = "Stale admin attempt";
        using var staleAdminClient = factory.CreateAuthorizedClient(
            demotedAdmin.Id,
            "Admin");
        using var createDepartment = await staleAdminClient.PostAsJsonAsync(
            "/api/admin/departments",
            new { name = departmentName, code = "STALE" });

        Assert.Equal(HttpStatusCode.Forbidden, createDepartment.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedUser = await context.Users
            .SingleAsync(x => x.Id == demotedAdmin.Id);
        Assert.Equal("Employee", persistedUser.Role);
        Assert.False(await context.Departments
            .AnyAsync(x => x.Name == departmentName));
    }

    [Fact]
    public async Task DemotedManager_CannotUseStaleTokenToApprove_AndRequestRemainsPending()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var owner = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync("Manager");
        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            "Admin");
        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            "Manager");

        using var demote = await adminClient.PatchAsJsonAsync(
            $"/api/admin/users/{manager.Id}/role",
            new { role = "Employee" });
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);

        var request = await factory.AddRequestAsync(owner.Id);
        await factory.AddAssignmentAsync(request.Id, manager.Id);
        using var approve = await managerClient.PatchAsync(
            $"/api/requests/{request.Id}/approve",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedRequest = await context.ApprovalRequests
            .SingleAsync(x => x.Id == request.Id);
        var assignment = await context.ApprovalAssignments
            .SingleAsync(x => x.RequestId == request.Id);
        Assert.Equal(RequestStatus.Pending, persistedRequest.Status);
        Assert.Equal(ApprovalAssignmentStatus.Pending, assignment.Status);
    }

    [Fact]
    public async Task Model_HasRestrictiveRelationshipsAndRequiredIndexes()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var model = context.GetService<IDesignTimeModel>().Model;

        var requestEntity = model.FindEntityType(
            typeof(FlowDesk.Domain.Entities.ApprovalRequest))!;
        Assert.Contains(requestEntity.GetForeignKeys(), key =>
            key.Properties.Any(property =>
                property.Name == nameof(
                    FlowDesk.Domain.Entities.ApprovalRequest.CreatedByUserId)) &&
            key.DeleteBehavior == DeleteBehavior.NoAction);
        Assert.Contains(requestEntity.GetIndexes(), index =>
            index.Properties.Any(property =>
                property.Name == nameof(
                    FlowDesk.Domain.Entities.ApprovalRequest.CreatedByUserId)));
        Assert.Contains(requestEntity.GetForeignKeys(), key =>
            key.Properties.Any(property =>
                property.Name == nameof(
                    FlowDesk.Domain.Entities.ApprovalRequest.DepartmentId)) &&
            key.DeleteBehavior == DeleteBehavior.NoAction);

        var assignmentEntity = model.FindEntityType(
            typeof(FlowDesk.Domain.Entities.ApprovalAssignment))!;
        Assert.Contains(assignmentEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.GetFilter() == "[Status] = 1" &&
            index.Properties.Single().Name == "RequestId");
        Assert.All(assignmentEntity.GetForeignKeys(), key =>
            Assert.Equal(DeleteBehavior.NoAction, key.DeleteBehavior));

        var departmentEntity = model.FindEntityType(
            typeof(FlowDesk.Domain.Entities.Department))!;
        Assert.Contains(departmentEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Single().Name == "Name");
        Assert.Contains(departmentEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.GetFilter() == "[Code] IS NOT NULL" &&
            index.Properties.Single().Name == "Code");

        Assert.Contains(requestEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ApprovalRequests_Status");
        Assert.Contains(model.FindEntityType(
                typeof(FlowDesk.Domain.Entities.User))!
            .GetCheckConstraints(), constraint =>
            constraint.Name == "CK_Users_Role");
    }
}
