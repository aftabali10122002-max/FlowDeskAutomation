using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class ApprovalWorkflowTests
{
    [Fact]
    public async Task Notifications_ArePagedUnreadAndScopedToTheRecipient()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var employee = await factory.AddUserAsync();
        var otherEmployee = await factory.AddUserAsync();
        using var unauthenticatedClient = factory.CreateClient();
        using var deniedNegotiation = await unauthenticatedClient.PostAsync(
            "/hubs/notifications/negotiate?negotiateVersion=1",
            content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, deniedNegotiation.StatusCode);
        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            UserRoles.Employee);
        using var otherClient = factory.CreateAuthorizedClient(
            otherEmployee.Id,
            UserRoles.Employee);
        using var authorizedNegotiation = await employeeClient.PostAsync(
            "/hubs/notifications/negotiate?negotiateVersion=1",
            content: null);
        Assert.Equal(HttpStatusCode.OK, authorizedNegotiation.StatusCode);

        using var createRequest = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new { title = "Notification request", amount = 45m });
        Assert.Equal(HttpStatusCode.Created, createRequest.StatusCode);
        using var createdDocument = JsonDocument.Parse(
            await createRequest.Content.ReadAsStringAsync());
        var requestId = createdDocument.RootElement
            .GetProperty("id")
            .GetInt32();

        using var notificationsResponse = await employeeClient.GetAsync(
            "/api/notifications?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, notificationsResponse.StatusCode);
        using var notifications = JsonDocument.Parse(
            await notificationsResponse.Content.ReadAsStringAsync());
        var notification = Assert.Single(notifications.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToArray());
        Assert.Equal(requestId, notification.GetProperty("requestId").GetInt32());
        Assert.Equal("RequestSubmitted", notification.GetProperty("type").GetString());
        Assert.Equal(1, notifications.RootElement
            .GetProperty("unreadCount").GetInt32());
        var notificationId = notification.GetProperty("id").GetInt64();

        using var otherNotificationsResponse = await otherClient.GetAsync(
            "/api/notifications");
        using var otherNotifications = JsonDocument.Parse(
            await otherNotificationsResponse.Content.ReadAsStringAsync());
        Assert.Empty(otherNotifications.RootElement
            .GetProperty("items")
            .EnumerateArray());

        using var crossUserRead = await otherClient.PatchAsync(
            $"/api/notifications/{notificationId}/read",
            content: null);
        Assert.Equal(HttpStatusCode.NotFound, crossUserRead.StatusCode);

        using var markRead = await employeeClient.PatchAsync(
            $"/api/notifications/{notificationId}/read",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, markRead.StatusCode);
        using var readNotificationsResponse = await employeeClient.GetAsync(
            "/api/notifications");
        using var readNotifications = JsonDocument.Parse(
            await readNotificationsResponse.Content.ReadAsStringAsync());
        Assert.Equal(0, readNotifications.RootElement
            .GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task TwoStageWorkflow_RestrictsEachDecisionAndCompletesInOrder()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var department = new Department("Workflow department", "WFD");
        using (var departmentScope = factory.Services.CreateScope())
        {
            var departmentContext = departmentScope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            departmentContext.Departments.Add(department);
            await departmentContext.SaveChangesAsync();
        }

        var admin = await factory.AddUserAsync(UserRoles.Admin);
        var employee = await factory.AddUserAsync();
        var firstManager = await factory.AddUserAsync(UserRoles.Manager);
        var secondManager = await factory.AddUserAsync(UserRoles.Manager);
        var otherManager = await factory.AddUserAsync(UserRoles.Manager);
        await SetDepartmentAsync(factory, employee.Id, department.Id);
        await SetDepartmentAsync(factory, firstManager.Id, department.Id);
        await SetDepartmentAsync(factory, secondManager.Id, department.Id);
        await SetDepartmentAsync(factory, otherManager.Id, department.Id);

        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);
        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            UserRoles.Employee);
        using var firstManagerClient = factory.CreateAuthorizedClient(
            firstManager.Id,
            UserRoles.Manager);
        using var secondManagerClient = factory.CreateAuthorizedClient(
            secondManager.Id,
            UserRoles.Manager);
        using var otherManagerClient = factory.CreateAuthorizedClient(
            otherManager.Id,
            UserRoles.Manager);

        var workflowId = await CreateWorkflowAsync(
            adminClient,
            $"two-stage-{Guid.NewGuid():N}",
            department.Id,
            [
                Stage(1, firstManager.Id),
                Stage(2, secondManager.Id)
            ],
            priority: RequestPriority.High);
        using var activate = await adminClient.PostAsync(
            $"/api/admin/workflows/{workflowId}/activate",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);

        using var createRequest = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new
            {
                title = "Two-stage test request",
                amount = 875m,
                priority = RequestPriority.High
            });
        Assert.Equal(HttpStatusCode.Created, createRequest.StatusCode);
        using var createdDocument = JsonDocument.Parse(
            await createRequest.Content.ReadAsStringAsync());
        var requestId = createdDocument.RootElement
            .GetProperty("id")
            .GetInt32();

        using var workflowResponse = await employeeClient.GetAsync(
            $"/api/requests/{requestId}/workflow");
        Assert.Equal(HttpStatusCode.OK, workflowResponse.StatusCode);
        using var workflowDocument = JsonDocument.Parse(
            await workflowResponse.Content.ReadAsStringAsync());
        var stages = workflowDocument.RootElement
            .GetProperty("stages")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(2, stages.Length);
        Assert.EndsWith(
            "Z",
            workflowDocument.RootElement.GetProperty("startedAtUtc")
                .GetString());
        Assert.EndsWith(
            "Z",
            stages[0].GetProperty("activatedAtUtc").GetString());
        Assert.Equal(
            ApprovalWorkflowStageStatus.Active.ToString(),
            stages[0].GetProperty("status").GetString());
        Assert.Equal(
            ApprovalWorkflowStageStatus.Inactive.ToString(),
            stages[1].GetProperty("status").GetString());

        using var fallbackRequest = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new
            {
                title = "Low-priority manual request",
                amount = 875m,
                priority = RequestPriority.Low
            });
        Assert.Equal(HttpStatusCode.Created, fallbackRequest.StatusCode);
        using var fallbackDocument = JsonDocument.Parse(
            await fallbackRequest.Content.ReadAsStringAsync());
        var fallbackRequestId = fallbackDocument.RootElement
            .GetProperty("id")
            .GetInt32();
        using var noMatchedWorkflow = await employeeClient.GetAsync(
            $"/api/requests/{fallbackRequestId}/workflow");
        Assert.Equal(HttpStatusCode.NotFound, noMatchedWorkflow.StatusCode);

        using var wrongManagerDecision = await otherManagerClient
            .PatchAsJsonAsync(
                $"/api/requests/{requestId}/approve",
                new { comment = "Not assigned" });
        Assert.Equal(HttpStatusCode.NotFound, wrongManagerDecision.StatusCode);

        using var firstDecision = await firstManagerClient.PatchAsJsonAsync(
            $"/api/requests/{requestId}/approve",
            new { comment = "First stage approved" });
        Assert.Equal(HttpStatusCode.OK, firstDecision.StatusCode);
        using var staleStageDecision = await firstManagerClient.PatchAsJsonAsync(
            $"/api/requests/{requestId}/approve",
            new { comment = "The next stage is not mine" });
        Assert.Equal(HttpStatusCode.NotFound, staleStageDecision.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await context.ApprovalAssignments
                .Where(x => x.RequestId == fallbackRequestId)
                .ToListAsync());
            var request = await context.ApprovalRequests
                .SingleAsync(x => x.Id == requestId);
            Assert.Equal(RequestStatus.Pending, request.Status);

            var assignments = await context.ApprovalAssignments
                .Where(x => x.RequestId == requestId)
                .OrderBy(x => x.AssignedAtUtc)
                .ToListAsync();
            Assert.Equal(2, assignments.Count);
            Assert.Equal(
                ApprovalAssignmentStatus.Approved,
                assignments[0].Status);
            Assert.Equal(
                ApprovalAssignmentStatus.Pending,
                assignments[1].Status);
            Assert.Equal(secondManager.Id, assignments[1].ApproverUserId);
        }

        using var secondDecision = await secondManagerClient.PatchAsJsonAsync(
            $"/api/requests/{requestId}/approve",
            new { comment = "Final stage approved" });
        Assert.Equal(HttpStatusCode.OK, secondDecision.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var request = await context.ApprovalRequests
                .SingleAsync(x => x.Id == requestId);
            Assert.Equal(RequestStatus.Approved, request.Status);

            var workflow = await context.ApprovalWorkflowInstances
                .Include(x => x.Stages)
                    .ThenInclude(x => x.DecisionHistory)
                .SingleAsync(x => x.RequestId == requestId);
            Assert.Equal(ApprovalWorkflowStatus.Approved, workflow.Status);
            Assert.All(workflow.Stages, stage =>
            {
                Assert.Equal(ApprovalWorkflowStageStatus.Approved, stage.Status);
                Assert.NotNull(stage.DecisionHistory);
                Assert.NotNull(stage.DecidedAtUtc);
            });
            Assert.Equal(
                "First stage approved",
                workflow.Stages.Single(x => x.Sequence == 1)
                    .DecisionHistory!.Comment);
            Assert.Equal(
                "Final stage approved",
                workflow.Stages.Single(x => x.Sequence == 2)
                    .DecisionHistory!.Comment);
        }
    }

    [Fact]
    public async Task WorkflowRejection_RequiresAndStoresReason()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var department = new Department("Rejection department", "REJ");
        using (var departmentScope = factory.Services.CreateScope())
        {
            var departmentContext = departmentScope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            departmentContext.Departments.Add(department);
            await departmentContext.SaveChangesAsync();
        }

        var admin = await factory.AddUserAsync(UserRoles.Admin);
        var employee = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync(UserRoles.Manager);
        await SetDepartmentAsync(factory, employee.Id, department.Id);
        await SetDepartmentAsync(factory, manager.Id, department.Id);
        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);
        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            UserRoles.Employee);
        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            UserRoles.Manager);

        var workflowId = await CreateWorkflowAsync(
            adminClient,
            $"reject-{Guid.NewGuid():N}",
            department.Id,
            [Stage(1, manager.Id)]);
        using var activate = await adminClient.PostAsync(
            $"/api/admin/workflows/{workflowId}/activate",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);
        using var createRequest = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new { title = "Request needing review", amount = 120m });
        Assert.Equal(HttpStatusCode.Created, createRequest.StatusCode);
        using var createdDocument = JsonDocument.Parse(
            await createRequest.Content.ReadAsStringAsync());
        var requestId = createdDocument.RootElement
            .GetProperty("id")
            .GetInt32();

        using var missingReason = await managerClient.PatchAsync(
            $"/api/requests/{requestId}/reject",
            content: null);
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        using var rejection = await managerClient.PatchAsJsonAsync(
            $"/api/requests/{requestId}/reject",
            new { comment = "The request is outside the approved budget." });
        Assert.Equal(HttpStatusCode.OK, rejection.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = await context.ApprovalRequests
            .SingleAsync(x => x.Id == requestId);
        var workflow = await context.ApprovalWorkflowInstances
            .Include(x => x.Stages)
                .ThenInclude(x => x.DecisionHistory)
            .SingleAsync(x => x.RequestId == requestId);
        Assert.Equal(RequestStatus.Rejected, request.Status);
        Assert.Equal(ApprovalWorkflowStatus.Rejected, workflow.Status);
        Assert.Equal(
            "The request is outside the approved budget.",
            Assert.Single(workflow.Stages).DecisionHistory!.Comment);
    }

    [Fact]
    public async Task OverdueEscalation_CreatesOneNotificationAndCanBeMarkedRead()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync(UserRoles.Admin);
        var employee = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync(UserRoles.Manager);
        var escalationManager = await factory.AddUserAsync(UserRoles.Manager);
        var unrelatedEmployee = await factory.AddUserAsync();
        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);
        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            UserRoles.Employee);
        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            UserRoles.Manager);
        using var escalationClient = factory.CreateAuthorizedClient(
            escalationManager.Id,
            UserRoles.Manager);
        using var unrelatedClient = factory.CreateAuthorizedClient(
            unrelatedEmployee.Id,
            UserRoles.Employee);

        var workflowId = await CreateWorkflowAsync(
            adminClient,
            $"escalation-{Guid.NewGuid():N}",
            departmentId: null,
            [Stage(1, manager.Id, escalationManager.Id)]);
        using var activate = await adminClient.PostAsync(
            $"/api/admin/workflows/{workflowId}/activate",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);
        using var createRequest = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new { title = "Overdue test request", amount = 80m });
        Assert.Equal(HttpStatusCode.Created, createRequest.StatusCode);
        using var createdDocument = JsonDocument.Parse(
            await createRequest.Content.ReadAsStringAsync());
        var requestId = createdDocument.RootElement
            .GetProperty("id")
            .GetInt32();

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stageId = await context.ApprovalWorkflowStages
                .Where(x => x.WorkflowInstance.RequestId == requestId)
                .Select(x => x.Id)
                .SingleAsync();
            var activatedAtUtc = DateTime.UtcNow.AddHours(-2);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ApprovalWorkflowStages SET ActivatedAtUtc = {activatedAtUtc} WHERE Id = {stageId}");
        }

        using var process = await adminClient.PostAsync(
            "/api/admin/workflows/process-escalations",
            content: null);
        Assert.Equal(HttpStatusCode.OK, process.StatusCode);
        using var processDocument = JsonDocument.Parse(
            await process.Content.ReadAsStringAsync());
        Assert.Equal(1, processDocument.RootElement
            .GetProperty("processed").GetInt32());

        using var notificationsResponse = await escalationClient.GetAsync(
            "/api/notifications");
        Assert.Equal(HttpStatusCode.OK, notificationsResponse.StatusCode);
        using var notifications = JsonDocument.Parse(
            await notificationsResponse.Content.ReadAsStringAsync());
        var escalationNotifications = notifications.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToArray();
        var notification = Assert.Single(
            escalationNotifications,
            x => x.GetProperty("type").GetString() == "Escalation");
        var notificationId = notification.GetProperty("id").GetInt64();
        Assert.Contains(
            "overdue",
            notification.GetProperty("message").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            "Z",
            notification.GetProperty("createdAtUtc").GetString());
        var assignedManagerNotifications = await managerClient
            .GetFromJsonAsync<JsonElement>("/api/notifications");
        var assignedManagerNotification = Assert.Single(
            assignedManagerNotifications.GetProperty("items")
                .EnumerateArray());
        Assert.Equal(
            "RequestAssigned",
            assignedManagerNotification.GetProperty("type").GetString());
        var unrelatedNotifications = await unrelatedClient
            .GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Empty(unrelatedNotifications.GetProperty("items")
            .EnumerateArray());

        using var markRead = await escalationClient.PatchAsync(
            $"/api/notifications/{notificationId}/read",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, markRead.StatusCode);

        using var repeatedProcess = await adminClient.PostAsync(
            "/api/admin/workflows/process-escalations",
            content: null);
        using var repeatedDocument = JsonDocument.Parse(
            await repeatedProcess.Content.ReadAsStringAsync());
        Assert.Equal(0, repeatedDocument.RootElement
            .GetProperty("processed").GetInt32());
        var notificationsAfterRead = await escalationClient.GetFromJsonAsync<
            JsonElement>("/api/notifications");
        var notificationAfterRead = Assert.Single(
            notificationsAfterRead.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("type").GetString() == "Escalation");
        Assert.EndsWith(
            "Z",
            notificationAfterRead.GetProperty("readAtUtc").GetString());
    }

    [Fact]
    public async Task DeadlineReminder_IsDeduplicatedAndExcludesCompletedRequests()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync(UserRoles.Admin);
        var employee = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync(UserRoles.Manager);
        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);
        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            UserRoles.Employee);
        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            UserRoles.Manager);

        var workflowId = await CreateWorkflowAsync(
            adminClient,
            $"deadline-reminder-{Guid.NewGuid():N}",
            departmentId: null,
            [Stage(1, manager.Id)]);
        using var activate = await adminClient.PostAsync(
            $"/api/admin/workflows/{workflowId}/activate",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);

        var requestIds = new List<int>();
        foreach (var title in new[] { "Completed before reminder", "Still pending" })
        {
            using var response = await employeeClient.PostAsJsonAsync(
                "/api/requests",
                new { title, amount = 30m });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            requestIds.Add(document.RootElement.GetProperty("id").GetInt32());
        }

        using var decision = await managerClient.PatchAsJsonAsync(
            $"/api/requests/{requestIds[0]}/approve",
            new { comment = "Completed" });
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<IApprovalWorkflowRepository>();
            var processed = await repository.ProcessDeadlineRemindersAsync(
                DateTime.UtcNow.AddMinutes(2),
                reminderMinutesBeforeDeadline: 120);
            Assert.Equal(1, processed);
            Assert.Equal(
                0,
                await repository.ProcessDeadlineRemindersAsync(
                    DateTime.UtcNow.AddMinutes(2),
                    reminderMinutesBeforeDeadline: 120));
        }

        var managerNotifications = await managerClient
            .GetFromJsonAsync<JsonElement>("/api/notifications?pageSize=100");
        var reminders = managerNotifications.GetProperty("items")
            .EnumerateArray()
            .Where(x => x.GetProperty("type").GetString() == "DeadlineReminder")
            .ToArray();
        var reminder = Assert.Single(reminders);
        Assert.Equal(
            requestIds[1],
            reminder.GetProperty("requestId").GetInt32());
    }

    [Fact]
    public async Task NewRequest_IsNotCreatedWhenActiveRuleManagerHasBeenDeactivated()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync(UserRoles.Admin);
        var employee = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync(UserRoles.Manager);
        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            UserRoles.Admin);
        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            UserRoles.Employee);

        var workflowId = await CreateWorkflowAsync(
            adminClient,
            $"inactive-manager-{Guid.NewGuid():N}",
            departmentId: null,
            [Stage(1, manager.Id)]);
        using var activate = await adminClient.PostAsync(
            $"/api/admin/workflows/{workflowId}/activate",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);

        using var deactivate = await adminClient.PatchAsJsonAsync(
            $"/api/admin/users/{manager.Id}/active",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        using var createRequest = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new { title = "Must not route", amount = 400m });
        Assert.Equal(HttpStatusCode.BadRequest, createRequest.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await context.ApprovalRequests.ToListAsync());
    }

    private static async Task<int> CreateWorkflowAsync(
        HttpClient adminClient,
        string key,
        int? departmentId,
        object[] stages,
        RequestPriority? priority = null)
    {
        using var response = await adminClient.PostAsJsonAsync(
            "/api/admin/workflows",
            new
            {
                workflowKey = key,
                name = "Test approval workflow",
                departmentId,
                minimumAmount = 0m,
                maximumAmountExclusive = (decimal?)null,
                priority,
                stages
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetInt32();
    }

    private static object Stage(
        int sequence,
        int managerId,
        int? escalationManagerId = null) => new
    {
        sequence,
        approverType = "SpecificManager",
        approverDepartmentId = (int?)null,
        specificApproverUserId = managerId,
        deadlineMinutes = escalationManagerId.HasValue ? 30 : 60,
        escalationAfterMinutes = escalationManagerId.HasValue ? 60 : (int?)null,
        escalationApproverUserId = escalationManagerId
    };

    private static async Task SetDepartmentAsync(
        ApiFactory factory,
        int userId,
        int departmentId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await context.Users.SingleAsync(x => x.Id == userId);
        user.ChangeDepartment(departmentId);
        await context.SaveChangesAsync();
    }
}
