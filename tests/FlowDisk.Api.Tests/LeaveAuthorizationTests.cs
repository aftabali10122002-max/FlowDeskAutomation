using System.Net;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class LeaveAuthorizationTests
{
    [Fact]
    public async Task ManagerCanReadOnlyActiveEmployeeLeaveWithinDepartment()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var manager = await factory.AddUserAsync("Manager");
        var employee = await factory.AddUserAsync();
        var peerManager = await factory.AddUserAsync("Manager");
        var otherDepartmentEmployee = await factory.AddUserAsync();
        var department = new Department($"Leave security {Guid.NewGuid():N}");
        var otherDepartment = new Department($"Other security {Guid.NewGuid():N}");

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Departments.AddRange(department, otherDepartment);
            await context.SaveChangesAsync();
            foreach (var user in await context.Users
                         .Where(user => user.Id == manager.Id ||
                             user.Id == employee.Id ||
                             user.Id == peerManager.Id)
                         .ToListAsync())
            {
                user.ChangeDepartment(department.Id);
            }

            var otherEmployee = await context.Users
                .SingleAsync(user => user.Id == otherDepartmentEmployee.Id);
            otherEmployee.ChangeDepartment(otherDepartment.Id);
            context.LeaveBalanceLedgerEntries.Add(
                new LeaveBalanceLedgerEntry(
                    employee.Id,
                    1,
                    null,
                    null,
                    null,
                    LeaveLedgerEntryType.Adjustment,
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    1m,
                    $"private-adjustment:{Guid.NewGuid():N}",
                    DateTime.UtcNow,
                    "Confidential payroll correction",
                    admin.Id));
            await context.SaveChangesAsync();
        }

        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            "Manager");
        using var unauthorizedAdminClaimClient = factory.CreateAuthorizedClient(
            employee.Id,
            "Admin");
        using var adminClient = factory.CreateAuthorizedClient(admin.Id, "Admin");

        using var ownTeamResponse = await managerClient.GetAsync(
            $"/api/leave/employees/{employee.Id}/balances");
        Assert.Equal(HttpStatusCode.OK, ownTeamResponse.StatusCode);

        using var employeeHistoryResponse = await managerClient.GetAsync(
            $"/api/leave/employees/{employee.Id}/history");
        Assert.Equal(HttpStatusCode.OK, employeeHistoryResponse.StatusCode);
        Assert.DoesNotContain(
            "Confidential payroll correction",
            await employeeHistoryResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using var peerManagerResponse = await managerClient.GetAsync(
            $"/api/leave/employees/{peerManager.Id}/balances");
        Assert.Equal(HttpStatusCode.Forbidden, peerManagerResponse.StatusCode);

        using var otherDepartmentResponse = await managerClient.GetAsync(
            $"/api/leave/employees/{otherDepartmentEmployee.Id}/balances");
        Assert.Equal(HttpStatusCode.Forbidden, otherDepartmentResponse.StatusCode);

        using var historyResponse = await managerClient.GetAsync(
            $"/api/leave/employees/{peerManager.Id}/history");
        Assert.Equal(HttpStatusCode.Forbidden, historyResponse.StatusCode);

        using var adminEndpointResponse = await unauthorizedAdminClaimClient.GetAsync(
            "/api/admin/leave/types");
        Assert.Equal(HttpStatusCode.Forbidden, adminEndpointResponse.StatusCode);

        using var authorizedAdminResponse = await adminClient.GetAsync(
            "/api/admin/leave/types");
        Assert.Equal(HttpStatusCode.OK, authorizedAdminResponse.StatusCode);
    }
}
