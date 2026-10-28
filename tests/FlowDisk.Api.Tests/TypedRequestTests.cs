using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class TypedRequestTests
{
    [Fact]
    public async Task LeaveReservationAndApprovedCancellationAreLedgeredTransactionally()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync("Manager");
        await factory.AddDefaultCompanyLeavePolicyAsync(admin.Id);
        int departmentId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            var department = new FlowDesk.Domain.Entities.Department(
                $"Leave Team {Guid.NewGuid():N}");
            context.Departments.Add(department);
            await context.SaveChangesAsync();
            departmentId = department.Id;
            var employees = await context.Users
                .Where(user =>
                    user.Id == employee.Id || user.Id == manager.Id)
                .ToListAsync();
            foreach (var user in employees)
            {
                user.ChangeDepartment(departmentId);
            }

            await context.SaveChangesAsync();
        }

        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            "Admin");
        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");
        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            "Manager");
        var firstLeaveDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var endLeaveDate = firstLeaveDate.AddDays(1);

        using var submitted = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new
            {
                requestTypeCode = "LEAVE",
                leave = new
                {
                    leaveType = "Annual",
                    startDate = firstLeaveDate.ToString("yyyy-MM-dd"),
                    endDate = endLeaveDate.ToString("yyyy-MM-dd"),
                    reason = "Planned family leave."
                }
            });
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        using var submittedDocument = JsonDocument.Parse(
            await submitted.Content.ReadAsStringAsync());
        var leaveRequestId = submittedDocument.RootElement
            .GetProperty("id")
            .GetInt32();

        using var pendingBalances = await employeeClient.GetAsync(
            "/api/leave/me/balances");
        Assert.Equal(HttpStatusCode.OK, pendingBalances.StatusCode);
        using var pendingDocument = JsonDocument.Parse(
            await pendingBalances.Content.ReadAsStringAsync());
        var annualPending = pendingDocument.RootElement.EnumerateArray()
            .Single(balance =>
                balance.GetProperty("leaveTypeCode").GetString() == "ANNUAL");
        Assert.Equal(2m, annualPending.GetProperty("pending").GetDecimal());

        using var assignment = await adminClient.PostAsJsonAsync(
            $"/api/requests/{leaveRequestId}/assignments",
            new { approverUserId = manager.Id });
        Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        using var approval = await managerClient.PatchAsJsonAsync(
            $"/api/requests/{leaveRequestId}/approve",
            new { comment = "Approved." });
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);

        using var scheduledBalances = await employeeClient.GetAsync(
            "/api/leave/me/balances");
        using var scheduledDocument = JsonDocument.Parse(
            await scheduledBalances.Content.ReadAsStringAsync());
        var annualScheduled = scheduledDocument.RootElement.EnumerateArray()
            .Single(balance =>
                balance.GetProperty("leaveTypeCode").GetString() == "ANNUAL");
        Assert.Equal(0m, annualScheduled.GetProperty("pending").GetDecimal());
        Assert.Equal(2m, annualScheduled.GetProperty("scheduled").GetDecimal());

        using var cancellationRequest = await employeeClient.PostAsJsonAsync(
            $"/api/requests/{leaveRequestId}/cancellation-requests",
            new { reason = "Plans changed." });
        Assert.Equal(HttpStatusCode.Created, cancellationRequest.StatusCode);
        using var duplicateCancellationRequest =
            await employeeClient.PostAsJsonAsync(
                $"/api/requests/{leaveRequestId}/cancellation-requests",
                new { reason = "Duplicate cancellation attempt." });
        Assert.Equal(
            HttpStatusCode.BadRequest,
            duplicateCancellationRequest.StatusCode);
        using var cancellationDocument = JsonDocument.Parse(
            await cancellationRequest.Content.ReadAsStringAsync());
        var cancellationRequestId = cancellationDocument.RootElement
            .GetProperty("id")
            .GetInt32();
        using var cancellationAssignment = await adminClient.PostAsJsonAsync(
            $"/api/requests/{cancellationRequestId}/assignments",
            new { approverUserId = manager.Id });
        Assert.Equal(HttpStatusCode.Created, cancellationAssignment.StatusCode);
        using var cancellationApproval = await managerClient.PatchAsJsonAsync(
            $"/api/requests/{cancellationRequestId}/approve",
            new { comment = "Cancellation approved." });
        Assert.Equal(HttpStatusCode.OK, cancellationApproval.StatusCode);

        using var restoredBalances = await employeeClient.GetAsync(
            "/api/leave/me/balances");
        using var restoredDocument = JsonDocument.Parse(
            await restoredBalances.Content.ReadAsStringAsync());
        var annualRestored = restoredDocument.RootElement.EnumerateArray()
            .Single(balance =>
                balance.GetProperty("leaveTypeCode").GetString() == "ANNUAL");
        Assert.Equal(0m, annualRestored.GetProperty("pending").GetDecimal());
        Assert.Equal(0m, annualRestored.GetProperty("scheduled").GetDecimal());
        Assert.Equal(0m, annualRestored.GetProperty("available").GetDecimal());
    }

    [Fact]
    public async Task RejectedLeaveReleasesItsPendingReservation()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync("Manager");
        await factory.AddDefaultCompanyLeavePolicyAsync(admin.Id);
        using (var setupScope = factory.Services.CreateScope())
        {
            var setupContext = setupScope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            var department = new FlowDesk.Domain.Entities.Department(
                $"Rejected leave {Guid.NewGuid():N}");
            setupContext.Departments.Add(department);
            await setupContext.SaveChangesAsync();
            foreach (var user in await setupContext.Users
                         .Where(user =>
                             user.Id == employee.Id || user.Id == manager.Id)
                         .ToListAsync())
            {
                user.ChangeDepartment(department.Id);
            }

            await setupContext.SaveChangesAsync();
        }

        using var adminClient = factory.CreateAuthorizedClient(admin.Id, "Admin");
        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");
        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            "Manager");
        var leaveDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        using var submitted = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new
            {
                requestTypeCode = "LEAVE",
                leave = new
                {
                    leaveType = "Annual",
                    startDate = leaveDate.ToString("yyyy-MM-dd"),
                    endDate = leaveDate.ToString("yyyy-MM-dd"),
                    reason = "Private leave reason."
                }
            });
        Assert.True(
            submitted.StatusCode == HttpStatusCode.Created,
            await submitted.Content.ReadAsStringAsync());
        using var submittedDocument = JsonDocument.Parse(
            await submitted.Content.ReadAsStringAsync());
        var requestId = submittedDocument.RootElement
            .GetProperty("id")
            .GetInt32();

        using var assignment = await adminClient.PostAsJsonAsync(
            $"/api/requests/{requestId}/assignments",
            new { approverUserId = manager.Id });
        Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        using var rejection = await managerClient.PatchAsJsonAsync(
            $"/api/requests/{requestId}/reject",
            new { comment = "Rejected." });
        Assert.Equal(HttpStatusCode.OK, rejection.StatusCode);

        using var balances = await employeeClient.GetAsync(
            "/api/leave/me/balances");
        using var balanceDocument = JsonDocument.Parse(
            await balances.Content.ReadAsStringAsync());
        var annual = balanceDocument.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("leaveTypeCode").GetString() == "ANNUAL");
        Assert.Equal(0m, annual.GetProperty("pending").GetDecimal());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(
            1,
            await context.LeaveBalanceLedgerEntries.CountAsync(entry =>
                entry.RequestId == requestId &&
                entry.EntryType == FlowDesk.Domain.Enums.LeaveLedgerEntryType.Reservation));
        Assert.Equal(
            1,
            await context.LeaveBalanceLedgerEntries.CountAsync(entry =>
                entry.RequestId == requestId &&
                entry.EntryType == FlowDesk.Domain.Enums.LeaveLedgerEntryType.ReservationRelease));
    }

    [Fact]
    public async Task CrossYearLeaveRequestCalculatesAndReservesBothCalendarDates()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        await factory.AddDefaultCompanyLeavePolicyAsync(admin.Id);
        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");
        var startDate = new DateOnly(DateTime.UtcNow.Year + 1, 12, 31);
        var endDate = startDate.AddDays(1);

        using var response = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new
            {
                requestTypeCode = "LEAVE",
                leave = new
                {
                    leaveType = "Annual",
                    startDate = startDate.ToString("yyyy-MM-dd"),
                    endDate = endDate.ToString("yyyy-MM-dd"),
                    reason = "Cross-year leave request."
                }
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var requestId = document.RootElement.GetProperty("id").GetInt32();
        var details = document.RootElement.GetProperty("requestDetails");
        Assert.Equal(
            2m,
            details.GetProperty("calculatedLeaveUnits").GetDecimal());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var policyDays = await context.LeaveRequestPolicyDays
            .Where(day => day.RequestId == requestId)
            .OrderBy(day => day.LeaveDate)
            .ToListAsync();
        Assert.Equal(
            new[] { startDate, endDate },
            policyDays.Select(day => day.LeaveDate));
        Assert.All(policyDays, day => Assert.Equal(1m, day.Units));
    }

    [Fact]
    public async Task DueScheduledLeaveIsConvertedToUsedLeaveIdempotently()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync("Manager");
        await factory.AddDefaultCompanyLeavePolicyAsync(admin.Id);
        int departmentId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            var department = new FlowDesk.Domain.Entities.Department(
                $"Leave Team {Guid.NewGuid():N}");
            context.Departments.Add(department);
            await context.SaveChangesAsync();
            departmentId = department.Id;
            foreach (var user in await context.Users.Where(user =>
                         user.Id == employee.Id || user.Id == manager.Id)
                     .ToListAsync())
            {
                user.ChangeDepartment(departmentId);
            }

            await context.SaveChangesAsync();
        }

        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            "Admin");
        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");
        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            "Manager");
        var leaveDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        using var submitted = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new
            {
                requestTypeCode = "LEAVE",
                leave = new
                {
                    leaveType = "Annual",
                    startDate = leaveDate.ToString("yyyy-MM-dd"),
                    endDate = leaveDate.ToString("yyyy-MM-dd"),
                    reason = "One day leave."
                }
            });
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        using var submittedDocument = JsonDocument.Parse(
            await submitted.Content.ReadAsStringAsync());
        var requestId = submittedDocument.RootElement
            .GetProperty("id")
            .GetInt32();
        using var assignment = await adminClient.PostAsJsonAsync(
            $"/api/requests/{requestId}/assignments",
            new { approverUserId = manager.Id });
        Assert.Equal(HttpStatusCode.Created, assignment.StatusCode);
        using var approval = await managerClient.PatchAsJsonAsync(
            $"/api/requests/{requestId}/approve",
            new { comment = "Approved." });
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<FlowDesk.Application.Interfaces.ILeaveAccountingRepository>();
            var now = DateTime.UtcNow.AddDays(3);
            Assert.Equal(
                1,
                await repository.ProcessDueScheduledCommitmentsAsync(now));
            Assert.Equal(
                0,
                await repository.ProcessDueScheduledCommitmentsAsync(now));
        }

        var asOf = leaveDate.AddDays(1);
        using var balanceResponse = await employeeClient.GetAsync(
            $"/api/leave/me/balances?asOf={asOf:yyyy-MM-dd}");
        using var balanceDocument = JsonDocument.Parse(
            await balanceResponse.Content.ReadAsStringAsync());
        var annual = balanceDocument.RootElement.EnumerateArray()
            .Single(balance =>
                balance.GetProperty("leaveTypeCode").GetString() == "ANNUAL");
        Assert.Equal(0m, annual.GetProperty("scheduled").GetDecimal());
        Assert.Equal(1m, annual.GetProperty("used").GetDecimal());
    }

    [Fact]
    public async Task EmployeeCanCreateAllTypedRequestsAndKeepAmountNullWhenNotApplicable()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        await factory.AddDefaultCompanyLeavePolicyAsync(admin.Id);
        var employee = await factory.AddUserAsync();
        using var client = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");

        var requests = new (string Code, object Payload, bool HasAmount)[]
        {
            (
                "MATERIAL_PURCHASE",
                new
                {
                    requestTypeCode = "MATERIAL_PURCHASE",
                    amount = 1250m,
                    priority = "High",
                    description = "Laptop purchase details.",
                    materialPurchase = new
                    {
                        itemName = "Laptop",
                        quantity = 2,
                        businessJustification = "Replace old equipment."
                    }
                },
                true),
            (
                "LEAVE",
                new
                {
                    requestTypeCode = "LEAVE",
                    priority = "Medium",
                    leave = new
                    {
                        leaveType = "Annual",
                        startDate = "2026-11-02",
                        endDate = "2026-11-03",
                        reason = "Family trip."
                    }
                },
                false),
            (
                "EXPENSE_REIMBURSEMENT",
                new
                {
                    requestTypeCode = "EXPENSE_REIMBURSEMENT",
                    amount = 86.50m,
                    priority = "Low",
                    expenseReimbursement = new
                    {
                        category = "Transport",
                        expenseDate = "2026-10-15",
                        businessJustification = "Client visit."
                    }
                },
                true),
            (
                "IT_ACCESS",
                new
                {
                    requestTypeCode = "IT_ACCESS",
                    priority = "Urgent",
                    itAccess = new
                    {
                        systemApplication = "Reporting",
                        requestedAccessLevel = "Editor",
                        durationDays = (int?)30,
                        businessJustification = "Required for assigned work."
                    }
                },
                false),
            (
                "TRAVEL",
                new
                {
                    requestTypeCode = "TRAVEL",
                    amount = 900m,
                    priority = "Medium",
                    travel = new
                    {
                        destination = "Chicago",
                        startDate = "2026-12-01",
                        endDate = "2026-12-02",
                        businessPurpose = "Attend planning workshop."
                    }
                },
                true),
            (
                "WORK_FROM_HOME",
                new
                {
                    requestTypeCode = "WORK_FROM_HOME",
                    priority = "Low",
                    workFromHome = new
                    {
                        startDate = "2026-11-10",
                        endDate = "2026-11-10",
                        reason = "Home maintenance appointment."
                    }
                },
                false)
        };

        var createdIds = new List<int>();
        foreach (var request in requests)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/requests",
                request.Payload);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            var body = document.RootElement;
            Assert.Equal(
                request.Code,
                body.GetProperty("requestTypeCode").GetString());
            Assert.Equal(
                request.HasAmount,
                body.GetProperty("amount").ValueKind != JsonValueKind.Null);
            Assert.Equal(JsonValueKind.Object, body.GetProperty("requestDetails").ValueKind);
            createdIds.Add(body.GetProperty("id").GetInt32());

            if (request.Code == "LEAVE")
            {
                Assert.Equal(
                    2,
                    body.GetProperty("requestDetails")
                        .GetProperty("totalLeaveDays")
                        .GetInt32());
            }

            if (request.Code == "MATERIAL_PURCHASE")
            {
                Assert.Equal(
                    "Laptop purchase details.",
                    body.GetProperty("description").GetString());
            }
        }

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedRequests = await context.ApprovalRequests
            .AsNoTracking()
            .Where(request => createdIds.Contains(request.Id))
            .ToListAsync();
        Assert.Equal(6, persistedRequests.Count);
        Assert.Equal(
            3,
            persistedRequests.Count(request => request.Amount is null));
    }

    [Fact]
    public async Task TypedRequestValidationRejectsAmountMismatchExtraDetailsAndLongDescription()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var employee = await factory.AddUserAsync();
        using var client = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");

        using var amountMismatch = await client.PostAsJsonAsync(
            "/api/requests",
            new
            {
                requestTypeCode = "LEAVE",
                amount = 25m,
                leave = new
                {
                    leaveType = "Annual",
                    startDate = "2026-11-02",
                    endDate = "2026-11-02",
                    reason = "Personal."
                }
            });
        Assert.Equal(HttpStatusCode.BadRequest, amountMismatch.StatusCode);

        using var extraDetails = await client.PostAsJsonAsync(
            "/api/requests",
            new
            {
                requestTypeCode = "LEAVE",
                leave = new
                {
                    leaveType = "Annual",
                    startDate = "2026-11-02",
                    endDate = "2026-11-02",
                    reason = "Personal."
                },
                workFromHome = new
                {
                    startDate = "2026-11-02",
                    endDate = "2026-11-02",
                    reason = "Personal."
                }
            });
        Assert.Equal(HttpStatusCode.BadRequest, extraDetails.StatusCode);

        using var longDescription = await client.PostAsJsonAsync(
            "/api/requests",
            new
            {
                requestTypeCode = "LEAVE",
                description = new string('x', 2001),
                leave = new
                {
                    leaveType = "Annual",
                    startDate = "2026-11-02",
                    endDate = "2026-11-02",
                    reason = "Personal."
                }
            });
        Assert.Equal(HttpStatusCode.BadRequest, longDescription.StatusCode);

        using var reversedDates = await client.PostAsJsonAsync(
            "/api/requests",
            new
            {
                requestTypeCode = "TRAVEL",
                amount = 500m,
                travel = new
                {
                    destination = "Seattle",
                    startDate = "2026-12-03",
                    endDate = "2026-12-02",
                    businessPurpose = "Planning meeting."
                }
            });
        Assert.Equal(HttpStatusCode.BadRequest, reversedDates.StatusCode);

        var admin = await factory.AddUserAsync("Admin");
        using var adminClient = factory.CreateAuthorizedClient(admin.Id, "Admin");
        using var disableLeave = await adminClient.PatchAsJsonAsync(
            "/api/admin/request-types/2/enabled",
            new { isEnabled = false });
        Assert.Equal(HttpStatusCode.OK, disableLeave.StatusCode);
        using var disabledType = await client.PostAsJsonAsync(
            "/api/requests",
            new
            {
                requestTypeCode = "LEAVE",
                leave = new
                {
                    leaveType = "Annual",
                    startDate = "2026-11-02",
                    endDate = "2026-11-02",
                    reason = "Personal."
                }
            });
        Assert.Equal(HttpStatusCode.BadRequest, disabledType.StatusCode);
    }

    [Fact]
    public async Task ExistingGenericRequestPayloadRemainsCompatible()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var employee = await factory.AddUserAsync();
        using var client = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");

        using var response = await client.PostAsJsonAsync(
            "/api/requests",
            new
            {
                title = "Legacy-compatible request",
                amount = 42m,
                description = "An existing generic request shape."
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "Legacy-compatible request",
            body.GetProperty("title").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            body.GetProperty("requestTypeId").ValueKind);
        Assert.Equal(
            "An existing generic request shape.",
            body.GetProperty("description").GetString());
    }

    [Fact]
    public async Task WorkflowRoutingUsesTypeAndDoesNotTreatNullAmountAsZero()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        var manager = await factory.AddUserAsync("Manager");
        await factory.AddDefaultCompanyLeavePolicyAsync(admin.Id);
        using var adminClient = factory.CreateAuthorizedClient(admin.Id, "Admin");
        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");

        using var ruleResponse = await adminClient.PostAsJsonAsync(
            "/api/admin/workflows",
            new
            {
                workflowKey = $"typed-financial-{Guid.NewGuid():N}",
                name = "Financial purchase routing",
                requestTypeId = 1,
                departmentId = (int?)null,
                minimumAmount = 100m,
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
            });
        Assert.Equal(HttpStatusCode.Created, ruleResponse.StatusCode);
        using var ruleDocument = JsonDocument.Parse(
            await ruleResponse.Content.ReadAsStringAsync());
        var ruleId = ruleDocument.RootElement.GetProperty("id").GetInt32();
        using var activate = await adminClient.PostAsync(
            $"/api/admin/workflows/{ruleId}/activate",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);

        using var materialResponse = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new
            {
                requestTypeCode = "MATERIAL_PURCHASE",
                amount = 125m,
                materialPurchase = new
                {
                    itemName = "Monitor",
                    quantity = 1,
                    businessJustification = "Required for work."
                }
            });
        Assert.Equal(HttpStatusCode.Created, materialResponse.StatusCode);
        using var materialDocument = JsonDocument.Parse(
            await materialResponse.Content.ReadAsStringAsync());
        var materialRequestId = materialDocument.RootElement
            .GetProperty("id")
            .GetInt32();
        using var routedWorkflow = await employeeClient.GetAsync(
            $"/api/requests/{materialRequestId}/workflow");
        Assert.Equal(HttpStatusCode.OK, routedWorkflow.StatusCode);

        using var leaveResponse = await employeeClient.PostAsJsonAsync(
            "/api/requests",
            new
            {
                requestTypeCode = "LEAVE",
                leave = new
                {
                    leaveType = "Annual",
                    startDate = "2026-11-02",
                    endDate = "2026-11-02",
                    reason = "Personal."
                }
            });
        Assert.Equal(HttpStatusCode.Created, leaveResponse.StatusCode);
        using var leaveDocument = JsonDocument.Parse(
            await leaveResponse.Content.ReadAsStringAsync());
        var leaveRequestId = leaveDocument.RootElement
            .GetProperty("id")
            .GetInt32();
        using var unroutedWorkflow = await employeeClient.GetAsync(
            $"/api/requests/{leaveRequestId}/workflow");
        Assert.Equal(HttpStatusCode.NotFound, unroutedWorkflow.StatusCode);
        Assert.Equal(
            JsonValueKind.Null,
            leaveDocument.RootElement.GetProperty("amount").ValueKind);
    }
}
