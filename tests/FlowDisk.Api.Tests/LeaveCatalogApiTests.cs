using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class LeaveCatalogApiTests
{
    [Fact]
    public async Task PolicyEndpointsReturnCycleFreePolicyResponses()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        using var client = factory.CreateAuthorizedClient(admin.Id, "Admin");

        using var createResponse = await client.PostAsJsonAsync(
            "/api/admin/leave/policies",
            new
            {
                code = "API-POLICY-TEST",
                name = "API policy response test",
                version = 1,
                scope = "Company",
                departmentId = (int?)null,
                designationId = (int?)null,
                employmentType = (string?)null,
                effectiveFrom = new DateOnly(2026, 1, 1),
                effectiveTo = (DateOnly?)null,
                dayCalculationMethod = "CalendarDays",
                leaveWorkScheduleId = (int?)null,
                leaveHolidayCalendarId = (int?)null,
                allowsHalfDays = true,
                halfDayUnits = 0.5m,
                isAccrualEnabled = false,
                accrualFrequency = (string?)null,
                allowsCarryForward = false,
                carryForwardCap = (decimal?)null,
                carryForwardExpiryMonths = (int?)null,
                probationPeriodDays = (int?)null,
                allowsLeaveDuringProbation = true,
                allowsNegativeBalance = false,
                leavePayrollCalendarId = (int?)null,
                entitlements = new[]
                {
                    new { leaveTypeId = 1, annualEntitlement = 20m },
                    new { leaveTypeId = 2, annualEntitlement = 10m },
                    new { leaveTypeId = 3, annualEntitlement = 8m },
                    new { leaveTypeId = 4, annualEntitlement = 0m }
                }
            });
        Assert.Equal(
            HttpStatusCode.Created,
            createResponse.StatusCode);
        using var createdJson = JsonDocument.Parse(
            await createResponse.Content.ReadAsStringAsync());
        var policyId = createdJson.RootElement
            .GetProperty("id")
            .GetInt32();
        Assert.Equal("Draft", createdJson.RootElement
            .GetProperty("status")
            .GetString());
        Assert.Equal(
            4,
            createdJson.RootElement.GetProperty("entitlements")
                .GetArrayLength());

        using var listResponse = await client.GetAsync(
            "/api/admin/leave/policies");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listJson = JsonDocument.Parse(
            await listResponse.Content.ReadAsStringAsync());
        Assert.Contains(
            listJson.RootElement.EnumerateArray(),
            policy => policy.GetProperty("id").GetInt32() == policyId);
    }

    [Fact]
    public async Task CatalogOperationsRequirePersistedAdminAndRejectOverlappingPayPeriods()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            "Admin");
        using var forgedAdminClient = factory.CreateAuthorizedClient(
            employee.Id,
            "Admin");

        using var unauthorized = await forgedAdminClient.GetAsync(
            "/api/admin/leave/work-schedules");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);

        var manager = await factory.AddUserAsync("Manager");
        using var profileForManager = await adminClient.PostAsJsonAsync(
            "/api/admin/leave/employees/metadata",
            new
            {
                employeeUserId = manager.Id,
                designationId = (int?)null,
                employmentType = (string?)null,
                hireDate = (DateOnly?)null,
                effectiveFrom = new DateOnly(2026, 1, 1),
                effectiveTo = (DateOnly?)null
            });
        Assert.Equal(HttpStatusCode.NotFound, profileForManager.StatusCode);

        using var designationResponse = await adminClient.PostAsJsonAsync(
            "/api/admin/leave/designations",
            new { code = "QA-ENGINEER", name = "QA Engineer" });
        Assert.Equal(HttpStatusCode.Created, designationResponse.StatusCode);
        using var designationJson = JsonDocument.Parse(
            await designationResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "QA-ENGINEER",
            designationJson.RootElement.GetProperty("code").GetString());

        using var designations = await adminClient.GetAsync(
            "/api/admin/leave/designations");
        using var designationsJson = JsonDocument.Parse(
            await designations.Content.ReadAsStringAsync());
        Assert.Contains(
            designationsJson.RootElement.EnumerateArray(),
            item => item.GetProperty("name").GetString() == "QA Engineer");

        using var forgedDesignation = await forgedAdminClient.PostAsJsonAsync(
            "/api/admin/leave/designations",
            new { code = "FORGED", name = "Forged designation" });
        Assert.Equal(HttpStatusCode.Forbidden, forgedDesignation.StatusCode);

        using var duplicateDesignation = await adminClient.PostAsJsonAsync(
            "/api/admin/leave/designations",
            new { code = "QA-ENGINEER", name = "Duplicate name" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateDesignation.StatusCode);

        using var scheduleResponse = await adminClient.PostAsJsonAsync(
            "/api/admin/leave/work-schedules",
            new
            {
                code = "STANDARD",
                name = "Standard work week",
                days = Enumerable.Range(0, 7).Select(day => new
                {
                    dayOfWeek = day,
                    isWorkingDay = day is >= 1 and <= 5,
                    standardHours = day is >= 1 and <= 5 ? 8m : 0m
                })
            });
        Assert.True(
            scheduleResponse.IsSuccessStatusCode,
            await scheduleResponse.Content.ReadAsStringAsync());
        using var scheduleJson = JsonDocument.Parse(
            await scheduleResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            7,
            scheduleJson.RootElement.GetProperty("days").GetArrayLength());

        using var calendarResponse = await adminClient.PostAsJsonAsync(
            "/api/admin/leave/payroll-calendars",
            new
            {
                code = "BIWEEKLY",
                name = "Biweekly",
                timeZoneId = "UTC",
                expectedPeriodsInEntitlementYear = 26
            });
        Assert.Equal(HttpStatusCode.OK, calendarResponse.StatusCode);
        using var calendarJson = JsonDocument.Parse(
            await calendarResponse.Content.ReadAsStringAsync());
        var calendarId = calendarJson.RootElement
            .GetProperty("id")
            .GetInt32();

        using var periodResponse = await adminClient.PostAsJsonAsync(
            $"/api/admin/leave/payroll-calendars/{calendarId}/periods",
            new
            {
                startDate = "2026-01-01",
                endDate = "2026-01-14",
                postingDate = "2026-01-15",
                entitlementYear = 2026,
                periodNumber = 1
            });
        Assert.Equal(HttpStatusCode.OK, periodResponse.StatusCode);

        using var overlappingPeriod = await adminClient.PostAsJsonAsync(
            $"/api/admin/leave/payroll-calendars/{calendarId}/periods",
            new
            {
                startDate = "2026-01-14",
                endDate = "2026-01-27",
                postingDate = "2026-01-28",
                entitlementYear = 2026,
                periodNumber = 2
            });
        Assert.Equal(HttpStatusCode.BadRequest, overlappingPeriod.StatusCode);
    }
}
