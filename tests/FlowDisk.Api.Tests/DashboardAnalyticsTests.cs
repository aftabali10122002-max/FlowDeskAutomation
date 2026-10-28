using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class DashboardAnalyticsTests
{
    [Fact]
    public async Task EmployeeAnalytics_OnlyIncludesOwnedRequestsWithinInclusiveUtcDates()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var employee = await factory.AddUserAsync();
        var otherEmployee = await factory.AddUserAsync();
        var included = await factory.AddRequestAsync(employee.Id);
        var excludedOwner = await factory.AddRequestAsync(otherEmployee.Id);

        await SetCreatedAtAsync(factory, included.Id, new DateTime(
            2026, 10, 1, 23, 59, 59, DateTimeKind.Utc));
        await SetCreatedAtAsync(factory, excludedOwner.Id, new DateTime(
            2026, 10, 15, 12, 0, 0, DateTimeKind.Utc));

        using var client = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");
        using var response = await client.GetAsync(
            "/api/analytics/dashboard?from=2026-10-01&to=2026-10-01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("totalRequests").GetInt32());
        Assert.Equal("2026-10-01", root.GetProperty("from").GetString());
        Assert.Equal("2026-10-01", root.GetProperty("to").GetString());
        Assert.Equal(1, CountFor(
            root.GetProperty("statusDistribution"),
            "Pending"));
        Assert.Equal(1, CountFor(
            root.GetProperty("approvalProgress"),
            "Awaiting routing"));
        Assert.Equal(1, root.GetProperty("employeeSummary")
            .GetProperty("pendingRequests").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("adminSummary").ValueKind);
    }

    [Fact]
    public async Task ManagerAnalytics_DeduplicatesRequestsAndCountsOnlyAssignedRequests()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var manager = await factory.AddUserAsync("Manager");
        var employee = await factory.AddUserAsync();
        var assigned = await factory.AddRequestAsync(employee.Id);
        var unassigned = await factory.AddRequestAsync(employee.Id);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            var olderAssignment = new ApprovalAssignment(
                assigned.Id,
                manager.Id);
            context.ApprovalAssignments.Add(olderAssignment);
            context.Entry(olderAssignment)
                .Property(x => x.AssignedAtUtc)
                .CurrentValue = new DateTime(
                    2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);
            await context.SaveChangesAsync();

            var latestAssignment = new ApprovalAssignment(
                assigned.Id,
                manager.Id);
            latestAssignment.MarkApproved(new DateTime(
                2026, 10, 4, 10, 0, 0, DateTimeKind.Utc));
            context.ApprovalAssignments.Add(latestAssignment);
            context.Entry(latestAssignment)
                .Property(x => x.AssignedAtUtc)
                .CurrentValue = new DateTime(
                    2026, 10, 4, 9, 0, 0, DateTimeKind.Utc);
            var trackedAssigned = await context.ApprovalRequests
                .SingleAsync(x => x.Id == assigned.Id);
            context.Entry(trackedAssigned)
                .Property(x => x.CreatedAtUtc)
                .CurrentValue = new DateTime(
                    2026, 10, 4, 10, 0, 0, DateTimeKind.Utc);
            var trackedUnassigned = await context.ApprovalRequests
                .SingleAsync(x => x.Id == unassigned.Id);
            context.Entry(trackedUnassigned)
                .Property(x => x.CreatedAtUtc)
                .CurrentValue = new DateTime(
                    2026, 10, 4, 11, 0, 0, DateTimeKind.Utc);
            await context.SaveChangesAsync();
        }

        using var client = factory.CreateAuthorizedClient(
            manager.Id,
            "Manager");
        using var response = await client.GetAsync(
            "/api/analytics/dashboard?from=2026-10-01&to=2026-10-07");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("totalRequests").GetInt32());
        Assert.Equal(1, CountFor(
            root.GetProperty("managerRequestStatus"),
            "Completed"));
        Assert.Equal(1, root.GetProperty("assignedRequestTrend")
            .EnumerateArray()
            .Sum(point => point.GetProperty("count").GetInt32()));
        Assert.Equal("Unspecified", root.GetProperty("priorityDistribution")
            .EnumerateArray()
            .Single(point => point.GetProperty("label").GetString() == "Unspecified")
            .GetProperty("label").GetString());
        var managerSummary = root.GetProperty("managerSummary");
        Assert.Equal(1, managerSummary.GetProperty("assignedRequests").GetInt32());
        Assert.Equal(0, managerSummary.GetProperty("pendingApprovals").GetInt32());
        Assert.Equal(1, managerSummary.GetProperty("approvedRequests").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("adminSummary").ValueKind);
    }

    [Fact]
    public async Task AdminAnalytics_IncludesLegacyRequestsAsOtherLegacy()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        var owned = await factory.AddRequestAsync(employee.Id);
        var legacy = await factory.AddRequestAsync(ownerUserId: null);
        var rangeDate = new DateTime(
            2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        await SetCreatedAtAsync(factory, owned.Id, rangeDate);
        await SetCreatedAtAsync(factory, legacy.Id, rangeDate);

        using var client = factory.CreateAuthorizedClient(admin.Id, "Admin");
        using var response = await client.GetAsync(
            "/api/analytics/dashboard?from=2026-10-12&to=2026-10-12");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("totalRequests").GetInt32());
        Assert.Equal(2, root.GetProperty("requestTypes")
            .EnumerateArray()
            .Single(item =>
                item.GetProperty("code").GetString() == "OTHER_LEGACY")
            .GetProperty("count").GetInt32());
        Assert.Equal(2, CountFor(
            root.GetProperty("priorityDistribution"),
            "Unspecified"));
        var adminSummary = root.GetProperty("adminSummary");
        Assert.Equal(2, adminSummary.GetProperty("totalRequests").GetInt32());
        Assert.Equal(2, adminSummary.GetProperty("pendingRequests").GetInt32());
        Assert.Equal(1, adminSummary.GetProperty("activeEmployees").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("employeeSummary").ValueKind);
    }

    [Fact]
    public async Task AdminAnalytics_ReportsOverdueWorkflowBottlenecksByStage()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var manager = await factory.AddUserAsync("Manager");
        var employee = await factory.AddUserAsync();
        var request = await factory.AddRequestAsync(employee.Id);
        var activatedAtUtc = DateTime.UtcNow.AddHours(-2);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            var ruleStage = new ApprovalWorkflowRuleStage(
                1,
                WorkflowApproverType.SpecificManager,
                approverDepartmentId: null,
                specificApproverUserId: manager.Id,
                deadlineMinutes: 30,
                escalationAfterMinutes: null,
                escalationApproverUserId: null);
            var rule = new ApprovalWorkflowRule(
                $"analytics-bottleneck-{Guid.NewGuid():N}",
                "Analytics bottleneck test workflow",
                1,
                departmentId: null,
                minimumAmount: 0m,
                maximumAmountExclusive: null,
                priority: null,
                [ruleStage]);
            rule.Activate(activatedAtUtc);
            await context.ApprovalWorkflowRules.AddAsync(rule);
            await context.SaveChangesAsync();

            var stage = new ApprovalWorkflowStage(
                1,
                manager.Id,
                deadlineMinutes: 30,
                escalationAfterMinutes: null,
                escalationApproverUserId: null);
            stage.Activate(activatedAtUtc);
            var workflow = new ApprovalWorkflowInstance(
                request.Id,
                rule,
                [stage],
                activatedAtUtc);
            await context.ApprovalWorkflowInstances.AddAsync(workflow);
            await context.SaveChangesAsync();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .ToString("yyyy-MM-dd");
        using var client = factory.CreateAuthorizedClient(admin.Id, "Admin");
        using var response = await client.GetAsync(
            $"/api/analytics/dashboard?from={today}&to={today}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var stageSummary = root.GetProperty("workflowBottlenecks")
            .EnumerateArray()
            .Single(item => item.GetProperty("stage").GetString() == "Stage 1");
        Assert.Equal(1, stageSummary.GetProperty("pendingApprovals").GetInt32());
        Assert.Equal(1, stageSummary.GetProperty("overdueApprovals").GetInt32());
        Assert.True(stageSummary.GetProperty("averageAgeHours").GetDouble() > 0);
        Assert.Equal(1, root.GetProperty("adminSummary")
            .GetProperty("overdueRequests").GetInt32());
    }

    [Fact]
    public async Task ManagerAnalytics_UsesOnlyOwnDecisionHistoryAndValidElapsedTime()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var manager = await factory.AddUserAsync("Manager");
        var otherManager = await factory.AddUserAsync("Manager");
        var employee = await factory.AddUserAsync();
        var ownRequest = await factory.AddRequestAsync(employee.Id);
        var otherRequest = await factory.AddRequestAsync(employee.Id);
        var manualRequest = await factory.AddRequestAsync(employee.Id);
        await factory.AddAssignmentAsync(ownRequest.Id, manager.Id);
        await factory.AddAssignmentAsync(otherRequest.Id, otherManager.Id);
        await AddDecisionAsync(
            factory,
            ownRequest.Id,
            manager.Id,
            new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc));
        await AddDecisionAsync(
            factory,
            otherRequest.Id,
            otherManager.Id,
            new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 5, 20, 0, 0, DateTimeKind.Utc));
        await AddManualDecisionAsync(
            factory,
            manualRequest.Id,
            manager.Id,
            new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 5, 11, 0, 0, DateTimeKind.Utc));

        using var client = factory.CreateAuthorizedClient(
            manager.Id,
            "Manager");
        using var response = await client.GetAsync(
            "/api/analytics/dashboard?from=2026-10-01&to=2026-10-07");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("decisionActivity")
            .EnumerateArray()
            .Sum(point => point.GetProperty("count").GetInt32()));
        Assert.Equal(
            1.5d,
            root.GetProperty("averageDecisionTimeHours").GetDouble());
    }

    [Fact]
    public async Task Analytics_ForbidsStaleRoleAndInactiveUsers()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var employee = await factory.AddUserAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            var user = await context.Users.SingleAsync(x => x.Id == employee.Id);
            context.Entry(user).Property(x => x.IsActive).CurrentValue = false;
            await context.SaveChangesAsync();
        }

        using var staleRoleClient = factory.CreateAuthorizedClient(
            employee.Id,
            "Admin");
        using var staleRoleResponse = await staleRoleClient.GetAsync(
            "/api/analytics/dashboard?from=2026-10-01&to=2026-10-07");
        Assert.Equal(HttpStatusCode.Forbidden, staleRoleResponse.StatusCode);

        using var inactiveClient = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");
        using var inactiveResponse = await inactiveClient.GetAsync(
            "/api/analytics/dashboard?from=2026-10-01&to=2026-10-07");
        Assert.Equal(HttpStatusCode.Forbidden, inactiveResponse.StatusCode);
    }

    [Fact]
    public async Task Analytics_RejectsInvalidOrOverlongDateRanges()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var employee = await factory.AddUserAsync();
        using var client = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");

        using var reversed = await client.GetAsync(
            "/api/analytics/dashboard?from=2026-10-08&to=2026-10-07");
        using var overlong = await client.GetAsync(
            "/api/analytics/dashboard?from=2025-10-01&to=2026-10-02");

        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, overlong.StatusCode);
    }

    private static async Task SetCreatedAtAsync(
        ApiFactory factory,
        int requestId,
        DateTime createdAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = await context.ApprovalRequests
            .SingleAsync(x => x.Id == requestId);
        context.Entry(request).Property(x => x.CreatedAtUtc).CurrentValue =
            createdAtUtc;
        await context.SaveChangesAsync();
    }

    private static async Task AddDecisionAsync(
        ApiFactory factory,
        int requestId,
        int managerId,
        DateTime activatedAtUtc,
        DateTime decidedAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ruleStage = new ApprovalWorkflowRuleStage(
            1,
            WorkflowApproverType.SpecificManager,
            approverDepartmentId: null,
            specificApproverUserId: managerId,
            deadlineMinutes: null,
            escalationAfterMinutes: null,
            escalationApproverUserId: null);
        var rule = new ApprovalWorkflowRule(
            $"analytics-{Guid.NewGuid():N}",
            "Analytics test workflow",
            1,
            departmentId: null,
            minimumAmount: 0m,
            maximumAmountExclusive: null,
            priority: null,
            [ruleStage]);
        rule.Activate(activatedAtUtc);
        context.ApprovalWorkflowRules.Add(rule);
        await context.SaveChangesAsync();

        var stage = new ApprovalWorkflowStage(
            1,
            managerId,
            deadlineMinutes: null,
            escalationAfterMinutes: null,
            escalationApproverUserId: null);
        stage.Activate(activatedAtUtc);
        stage.Decide(
            ApprovalWorkflowStageStatus.Approved,
            decidedAtUtc);
        var workflow = new ApprovalWorkflowInstance(
            requestId,
            rule,
            [stage],
            activatedAtUtc);
        context.ApprovalWorkflowInstances.Add(workflow);
        await context.SaveChangesAsync();

        context.ApprovalDecisionHistory.Add(new ApprovalDecisionHistory(
            stage.Id,
            managerId,
            ApprovalWorkflowStageStatus.Approved,
            comment: null,
            decidedAtUtc));
        await context.SaveChangesAsync();
    }

    private static async Task AddManualDecisionAsync(
        ApiFactory factory,
        int requestId,
        int managerId,
        DateTime assignedAtUtc,
        DateTime decidedAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignment = new ApprovalAssignment(requestId, managerId);
        assignment.MarkApproved(decidedAtUtc);
        context.ApprovalAssignments.Add(assignment);
        context.Entry(assignment)
            .Property(x => x.AssignedAtUtc)
            .CurrentValue = assignedAtUtc;
        await context.SaveChangesAsync();
    }

    private static int CountFor(JsonElement array, string label)
    {
        return array.EnumerateArray()
            .Where(item => item.GetProperty("label").GetString() == label)
            .Select(item => item.GetProperty("count").GetInt32())
            .Single();
    }
}
