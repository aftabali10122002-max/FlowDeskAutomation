using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowDesk.Domain.Entities;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class AuditLogTests
{
    [Fact]
    public async Task CriticalActions_ArePersistedWithActorsAndMinimalDetails()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync(UserRoles.Admin);
        var employee = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync(UserRoles.Manager);

        int departmentId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var department = new Department("Audit Test Department", "AUDIT");
            context.Departments.Add(department);
            await context.SaveChangesAsync();
            departmentId = department.Id;
            (await context.Users.SingleAsync(x => x.Id == employee.Id))
                .ChangeDepartment(departmentId);
            (await context.Users.SingleAsync(x => x.Id == manager.Id))
                .ChangeDepartment(departmentId);
            await context.SaveChangesAsync();
        }

        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);
        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            UserRoles.Employee);
        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            UserRoles.Manager);

        using var created = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new
            {
                title = "Highly confidential request title",
                amount = 125m,
                description = "This confidential description must not be logged."
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDocument = JsonDocument.Parse(
            await created.Content.ReadAsStringAsync());
        var requestId = createdDocument.RootElement
            .GetProperty("id")
            .GetInt32();

        using var assigned = await adminClient.PostAsJsonAsync(
            $"/api/requests/{requestId}/assignments",
            new { approverUserId = manager.Id });
        Assert.Equal(HttpStatusCode.Created, assigned.StatusCode);

        using var approved = await managerClient.PatchAsJsonAsync(
            $"/api/requests/{requestId}/approve",
            new { comment = "A confidential decision comment." });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        using var rejectedRequest = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new
            {
                title = "Confidential rejection request",
                amount = 80m,
                description = "Private rejection request details."
            });
        Assert.Equal(HttpStatusCode.Created, rejectedRequest.StatusCode);
        using var rejectedDocument = JsonDocument.Parse(
            await rejectedRequest.Content.ReadAsStringAsync());
        var rejectedRequestId = rejectedDocument.RootElement
            .GetProperty("id")
            .GetInt32();
        using var rejectedAssignment = await adminClient.PostAsJsonAsync(
            $"/api/requests/{rejectedRequestId}/assignments",
            new { approverUserId = manager.Id });
        Assert.Equal(HttpStatusCode.Created, rejectedAssignment.StatusCode);
        using var rejected = await managerClient.PatchAsJsonAsync(
            $"/api/requests/{rejectedRequestId}/reject",
            new { comment = "Private rejection reason." });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        var workflowInput = new
        {
            workflowKey = $"audit-{Guid.NewGuid():N}",
            name = "Audit test rule",
            departmentId = (int?)null,
            minimumAmount = 0m,
            maximumAmountExclusive = (decimal?)null,
            priority = (string?)null,
            stages = new[]
            {
                new
                {
                    sequence = 1,
                    approverType = "SpecificManager",
                    approverDepartmentId = (int?)null,
                    specificApproverUserId = manager.Id,
                    deadlineMinutes = (int?)null,
                    escalationAfterMinutes = (int?)null,
                    escalationApproverUserId = (int?)null
                }
            }
        };
        using var workflowCreated = await adminClient.PostAsJsonAsync(
            "/api/admin/workflows",
            workflowInput);
        Assert.Equal(HttpStatusCode.Created, workflowCreated.StatusCode);
        using var workflowDocument = JsonDocument.Parse(
            await workflowCreated.Content.ReadAsStringAsync());
        var workflowId = workflowDocument.RootElement
            .GetProperty("id")
            .GetInt32();

        using var workflowUpdated = await adminClient.PutAsJsonAsync(
            $"/api/admin/workflows/{workflowId}",
            new
            {
                workflowInput.workflowKey,
                name = "Updated audit test rule",
                workflowInput.departmentId,
                workflowInput.minimumAmount,
                workflowInput.maximumAmountExclusive,
                workflowInput.priority,
                workflowInput.stages
            });
        Assert.Equal(HttpStatusCode.OK, workflowUpdated.StatusCode);
        using var activated = await adminClient.PostAsync(
            $"/api/admin/workflows/{workflowId}/activate",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        using var deactivated = await adminClient.PostAsync(
            $"/api/admin/workflows/{workflowId}/deactivate",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);

        using var roleChanged = await adminClient.PatchAsJsonAsync(
            $"/api/admin/users/{employee.Id}/role",
            new { role = UserRoles.Manager });
        Assert.Equal(HttpStatusCode.OK, roleChanged.StatusCode);
        using var userDeactivated = await adminClient.PatchAsJsonAsync(
            $"/api/admin/users/{employee.Id}/active",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, userDeactivated.StatusCode);

        using var logsResponse = await adminClient.GetAsync(
            "/api/admin/audit-logs?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, logsResponse.StatusCode);
        using var logsDocument = JsonDocument.Parse(
            await logsResponse.Content.ReadAsStringAsync());
        var logs = logsDocument.RootElement.GetProperty("items")
            .EnumerateArray()
            .ToArray();

        AssertAuditActor(logs, "request.created", employee.Id, requestId);
        AssertAuditActor(logs, "request.assigned", admin.Id, requestId);
        AssertAuditActor(logs, "request.approved", manager.Id, requestId);
        AssertAuditActor(logs, "request.rejected", manager.Id, rejectedRequestId);
        AssertAuditActor(logs, "workflow_rule.created", admin.Id, workflowId);
        AssertAuditActor(logs, "workflow_rule.updated", admin.Id, workflowId);
        AssertAuditActor(logs, "workflow_rule.activated", admin.Id, workflowId);
        AssertAuditActor(logs, "workflow_rule.deactivated", admin.Id, workflowId);
        AssertAuditActor(logs, "user.role_changed", admin.Id, employee.Id);
        AssertAuditActor(logs, "user.deactivated", admin.Id, employee.Id);
        var serializedLogs = logsDocument.RootElement.GetRawText();
        Assert.DoesNotContain("Highly confidential request title", serializedLogs);
        Assert.DoesNotContain("confidential description", serializedLogs);
        Assert.DoesNotContain("confidential decision comment", serializedLogs);
        Assert.DoesNotContain("Confidential rejection request", serializedLogs);
        Assert.DoesNotContain("Private rejection reason", serializedLogs);

        using var unauthorizedLogs = await managerClient.GetAsync(
            "/api/admin/audit-logs");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedLogs.StatusCode);
    }

    [Fact]
    public async Task AuditHistory_IsPagedFilteredAndReadOnlyToAdministrators()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync(UserRoles.Admin);
        var employee = await factory.AddUserAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.AuditLogEntries.AddRange(
                new AuditLogEntry(
                    admin.Id,
                    "user.role_changed",
                    "User",
                    employee.Id,
                    DateTime.UtcNow.AddMinutes(-2),
                    """{"from":"Employee","to":"Manager"}"""),
                new AuditLogEntry(
                    admin.Id,
                    "user.deactivated",
                    "User",
                    employee.Id,
                    DateTime.UtcNow.AddMinutes(-1),
                    """{"from":true,"to":false}"""));
            await context.SaveChangesAsync();
        }

        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);
        using var response = await adminClient.GetAsync(
            "/api/admin/audit-logs?page=1&pageSize=1&action=user.&entityType=User");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("items").GetArrayLength());
        Assert.Equal(2, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(1, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(
            "user.deactivated",
            root.GetProperty("items")[0].GetProperty("action").GetString());

        using var overflowResponse = await adminClient.GetAsync(
            "/api/admin/audit-logs?page=2147483647&pageSize=100");
        Assert.Equal(HttpStatusCode.BadRequest, overflowResponse.StatusCode);
    }

    private static void AssertAuditActor(
        JsonElement[] logs,
        string action,
        int actorUserId,
        long entityId)
    {
        Assert.Contains(logs, log =>
            log.GetProperty("action").GetString() == action &&
            log.GetProperty("actorUserId").GetInt32() == actorUserId &&
            log.GetProperty("entityId").GetInt64() == entityId);
    }
}
