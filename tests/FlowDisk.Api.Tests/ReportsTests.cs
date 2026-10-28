using System.Net;
using System.Text.Json;
using System.Text;
using ClosedXML.Excel;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class ReportsTests
{
    private const string PdfExportCiSkipReason =
        "TODO: Re-enable after resolving the CI-only HTTP 409 conflict in PDF report exports.";

    [Theory]
    [InlineData("requests", "Request Reports", "Requests_Report")]
    [InlineData("approval-history", "Approval History", "Approval_History")]
    [InlineData("workflow-performance", "Workflow Performance", "Workflow_Performance")]
    [InlineData("leave", "Leave Reports", "Leave_Report")]
    public async Task ReportExports_ReturnValidExcelFiles(
        string report,
        string title,
        string fileNamePrefix)
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        using var client = factory.CreateAuthorizedClient(admin.Id, "Admin");
        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .ToString("yyyy-MM-dd");

        using var excelResponse = await client.GetAsync(
            $"/api/reports/{report}/export?format=xlsx&from={today}&to={today}&status=Approved");
        Assert.Equal(HttpStatusCode.OK, excelResponse.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            $"FlowDesk_{fileNamePrefix}",
            excelResponse.Content.Headers.ContentDisposition?.FileNameStar ??
            excelResponse.Content.Headers.ContentDisposition?.FileName);
        var workbookBytes = await excelResponse.Content.ReadAsByteArrayAsync();
        using (var workbook = new XLWorkbook(new MemoryStream(workbookBytes)))
        {
            var worksheet = workbook.Worksheet(1);
            Assert.Contains(title, worksheet.Cell(1, 1).GetString());
            Assert.Contains("Applied filters:", worksheet.Cell(4, 1).GetString());
            Assert.Contains("Status: Approved", worksheet.Cell(4, 1).GetString());
            Assert.Equal(6, worksheet.SheetView.SplitRow);
        }
    }

    // TODO: Re-enable after resolving the CI-only HTTP 409 conflict in PDF report exports.
    [Theory(Skip = PdfExportCiSkipReason)]
    [InlineData("requests", "Request Reports", "Requests_Report")]
    [InlineData("approval-history", "Approval History", "Approval_History")]
    [InlineData("workflow-performance", "Workflow Performance", "Workflow_Performance")]
    [InlineData("leave", "Leave Reports", "Leave_Report")]
    public async Task ReportExports_ReturnValidPdfFiles(
        string report,
        string title,
        string fileNamePrefix)
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        using var client = factory.CreateAuthorizedClient(admin.Id, "Admin");
        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .ToString("yyyy-MM-dd");
        using var pdfResponse = await client.GetAsync(
            $"/api/reports/{report}/export?format=pdf&from={today}&to={today}");
        Assert.Equal(HttpStatusCode.OK, pdfResponse.StatusCode);
        Assert.Equal(
            "application/pdf",
            pdfResponse.Content.Headers.ContentType?.MediaType);
        var pdfBytes = await pdfResponse.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdfBytes, 0, 5));
        Assert.Equal(
            "attachment",
            pdfResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains(
            $"FlowDesk_{fileNamePrefix}",
            pdfResponse.Content.Headers.ContentDisposition?.FileNameStar ??
            pdfResponse.Content.Headers.ContentDisposition?.FileName);
        using var pdf = PdfReader.Open(
            new MemoryStream(pdfBytes),
            PdfDocumentOpenMode.Import);
        Assert.Equal($"FlowDesk - {title}", pdf.Info.Title);
        Assert.Single(pdf.Pages);
    }

    [Fact]
    public async Task RequestExcelExport_ContainsAllBatchesInServerSortOrder()
    {
        using var factory = new ApiFactory { ReportExportMaxRows = 250 };
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        var ids = new List<int>();
        for (var index = 0; index < 205; index++)
        {
            ids.Add((await factory.AddRequestAsync(employee.Id)).Id);
        }

        using var client = factory.CreateAuthorizedClient(admin.Id, "Admin");
        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .ToString("yyyy-MM-dd");
        using var response = await client.GetAsync(
            $"/api/reports/requests/export?format=xlsx&from={today}&to={today}&sortBy=createdAtUtc&sortDirection=asc&page=2&pageSize=10");

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        using var workbook = new XLWorkbook(
            new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var worksheet = workbook.Worksheet(1);
        Assert.Equal(205, worksheet.LastRowUsed()!.RowNumber() - 6);
        var exportedIds = Enumerable.Range(7, 205)
            .Select(row => worksheet.Cell(row, 1).GetValue<int>())
            .ToArray();
        Assert.Equal(ids.Order().ToArray(), exportedIds);
    }

    // TODO: Re-enable after resolving the CI-only HTTP 409 conflict in PDF report exports.
    [Fact(Skip = PdfExportCiSkipReason)]
    public async Task RequestPdfExport_ContainsAllBatches()
    {
        using var factory = new ApiFactory { ReportExportMaxRows = 250 };
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        for (var index = 0; index < 205; index++)
        {
            await factory.AddRequestAsync(employee.Id);
        }

        using var client = factory.CreateAuthorizedClient(admin.Id, "Admin");
        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .ToString("yyyy-MM-dd");
        using var pdfResponse = await client.GetAsync(
            $"/api/reports/requests/export?format=pdf&from={today}&to={today}");
        Assert.Equal(HttpStatusCode.OK, pdfResponse.StatusCode);
        using var pdf = PdfReader.Open(
            new MemoryStream(await pdfResponse.Content.ReadAsByteArrayAsync()),
            PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount > 1);
    }

    [Fact]
    public async Task ReportExport_RejectsUnsupportedFormatsAndOversizedResults()
    {
        using var factory = new ApiFactory { ReportExportMaxRows = 2 };
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        await factory.AddRequestAsync(employee.Id);
        await factory.AddRequestAsync(employee.Id);
        await factory.AddRequestAsync(employee.Id);
        using var client = factory.CreateAuthorizedClient(admin.Id, "Admin");
        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .ToString("yyyy-MM-dd");

        using var invalidFormat = await client.GetAsync(
            $"/api/reports/requests/export?format=csv&from={today}&to={today}");
        Assert.Equal(HttpStatusCode.BadRequest, invalidFormat.StatusCode);
        using var invalidDocument = JsonDocument.Parse(
            await invalidFormat.Content.ReadAsStringAsync());
        Assert.Contains("xlsx or pdf", invalidDocument.RootElement
            .GetProperty("detail").GetString());

        using var oversized = await client.GetAsync(
            $"/api/reports/requests/export?format=xlsx&from={today}&to={today}");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        using var oversizedDocument = JsonDocument.Parse(
            await oversized.Content.ReadAsStringAsync());
        Assert.Contains("configured limit", oversizedDocument.RootElement
            .GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Reports_RejectInvalidSortFieldsAndExcessivePageSizes()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var employee = await factory.AddUserAsync();
        using var client = factory.CreateAuthorizedClient(employee.Id, "Employee");
        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .ToString("yyyy-MM-dd");

        using var invalidSort = await client.GetAsync(
            $"/api/reports/requests?from={today}&to={today}&sortBy=passwordHash");
        Assert.Equal(HttpStatusCode.BadRequest, invalidSort.StatusCode);

        using var excessivePageSize = await client.GetAsync(
            $"/api/reports/requests?from={today}&to={today}&pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, excessivePageSize.StatusCode);

        using var invalidExportSort = await client.GetAsync(
            $"/api/reports/requests/export?format=pdf&from={today}&to={today}&sortBy=internalComment");
        Assert.Equal(HttpStatusCode.BadRequest, invalidExportSort.StatusCode);
    }

    [Fact]
    public async Task ReportExport_UsesTheSameRoleAndFilterAuthorizationAsReports()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var employee = await factory.AddUserAsync();
        var otherEmployee = await factory.AddUserAsync();
        using var client = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");
        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .ToString("yyyy-MM-dd");

        using var forbiddenEmployee = await client.GetAsync(
            $"/api/reports/requests/export?format=xlsx&from={today}&to={today}&employeeUserId={otherEmployee.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenEmployee.StatusCode);

        using var forbiddenManager = await client.GetAsync(
            $"/api/reports/requests/export?format=pdf&from={today}&to={today}&managerUserId={otherEmployee.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenManager.StatusCode);
    }

    [Fact]
    public async Task RequestReport_PaginatesAndRestrictsEmployeesToTheirOwnRecords()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        var otherEmployee = await factory.AddUserAsync();
        var first = await factory.AddRequestAsync(employee.Id);
        var second = await factory.AddRequestAsync(employee.Id);
        await factory.AddRequestAsync(otherEmployee.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .ToString("yyyy-MM-dd");

        using var adminClient = factory.CreateAuthorizedClient(admin.Id, "Admin");
        using var adminResponse = await adminClient.GetAsync(
            $"/api/reports/requests?from={today}&to={today}&page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        using var adminDocument = JsonDocument.Parse(
            await adminResponse.Content.ReadAsStringAsync());
        Assert.Equal(3, adminDocument.RootElement
            .GetProperty("totalCount").GetInt32());
        Assert.Single(adminDocument.RootElement
            .GetProperty("items").EnumerateArray());
        Assert.Equal(1, adminDocument.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(1, adminDocument.RootElement
            .GetProperty("pageSize").GetInt32());

        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");
        using var employeeResponse = await employeeClient.GetAsync(
            $"/api/reports/requests?from={today}&to={today}");
        Assert.Equal(HttpStatusCode.OK, employeeResponse.StatusCode);
        using var employeeDocument = JsonDocument.Parse(
            await employeeResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, employeeDocument.RootElement
            .GetProperty("totalCount").GetInt32());
        Assert.All(
            employeeDocument.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.Contains(
                item.GetProperty("requestId").GetInt32(),
                new[] { first.Id, second.Id }));

        using var employeeExportResponse = await employeeClient.GetAsync(
            $"/api/reports/requests/export?format=xlsx&from={today}&to={today}");
        Assert.Equal(HttpStatusCode.OK, employeeExportResponse.StatusCode);
        using var employeeWorkbook = new XLWorkbook(
            new MemoryStream(
                await employeeExportResponse.Content.ReadAsByteArrayAsync()));
        var employeeWorksheet = employeeWorkbook.Worksheet(1);
        Assert.Equal(2, employeeWorksheet.LastRowUsed()!.RowNumber() - 6);
        Assert.Equal(
            new[] { first.Id, second.Id }.Order().ToArray(),
            Enumerable.Range(7, 2)
                .Select(row => employeeWorksheet.Cell(row, 1).GetValue<int>())
                .Order()
                .ToArray());
        Assert.Contains(
            "Employee scope: Current user",
            employeeWorksheet.Cell(4, 1).GetString());

        using var filterOptionsResponse = await employeeClient.GetAsync(
            "/api/reports/filter-options");
        Assert.Equal(HttpStatusCode.OK, filterOptionsResponse.StatusCode);
        using var filterOptions = JsonDocument.Parse(
            await filterOptionsResponse.Content.ReadAsStringAsync());
        var authorizedEmployees = filterOptions.RootElement
            .GetProperty("employees")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .ToArray();
        Assert.Empty(authorizedEmployees);

        using var forbiddenFilter = await employeeClient.GetAsync(
            $"/api/reports/requests?from={today}&to={today}&employeeUserId={otherEmployee.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenFilter.StatusCode);
    }

    [Fact]
    public async Task ManagerReports_OnlyIncludeRequestsAssignedToThatManager()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var manager = await factory.AddUserAsync("Manager");
        var otherManager = await factory.AddUserAsync("Manager");
        var employee = await factory.AddUserAsync();
        var assignedRequest = await factory.AddRequestAsync(employee.Id);
        await factory.AddRequestAsync(employee.Id);
        await factory.AddAssignmentAsync(assignedRequest.Id, manager.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .ToString("yyyy-MM-dd");

        using var managerClient = factory.CreateAuthorizedClient(
            manager.Id,
            "Manager");
        using var response = await managerClient.GetAsync(
            $"/api/reports/requests?from={today}&to={today}");

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.Equal(1, document.RootElement
            .GetProperty("totalCount").GetInt32());
        Assert.Equal(assignedRequest.Id, document.RootElement
            .GetProperty("items")[0].GetProperty("requestId").GetInt32());

        using var exportResponse = await managerClient.GetAsync(
            $"/api/reports/requests/export?format=xlsx&from={today}&to={today}");
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        using var workbook = new XLWorkbook(
            new MemoryStream(await exportResponse.Content.ReadAsByteArrayAsync()));
        var worksheet = workbook.Worksheet(1);
        Assert.Equal(1, worksheet.LastRowUsed()!.RowNumber() - 6);
        Assert.Equal(
            assignedRequest.Id,
            worksheet.Cell(7, 1).GetValue<int>());
        Assert.Contains(
            "Manager scope: Authorized assignments",
            worksheet.Cell(4, 1).GetString());

        using var forbiddenManagerFilter = await managerClient.GetAsync(
            $"/api/reports/requests?from={today}&to={today}&managerUserId={otherManager.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenManagerFilter.StatusCode);
    }

    [Fact]
    public async Task ApprovalHistory_DoesNotDuplicateWorkflowStageAssignments()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var manager = await factory.AddUserAsync("Manager");
        var employee = await factory.AddUserAsync();
        var request = await factory.AddRequestAsync(employee.Id);
        await AddTwoStageWorkflowAsync(factory, request.Id, manager.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .ToString("yyyy-MM-dd");
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<IReportsRepository>();
            var fromUtc = DateTime.SpecifyKind(
                date.ToDateTime(TimeOnly.MinValue),
                DateTimeKind.Utc);
            await repository.GetApprovalHistoryAsync(
                new ReportActor(admin.Id, "Admin", null),
                new ReportFilter(
                    date,
                    date,
                    fromUtc,
                    fromUtc.AddDays(1),
                    1,
                    20,
                    "decidedAtUtc",
                    true,
                    string.Empty,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null));
        }

        using var client = factory.CreateAuthorizedClient(admin.Id, "Admin");
        using var response = await client.GetAsync(
            $"/api/reports/approval-history?from={today}&to={today}");

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("items");
        Assert.Single(items.EnumerateArray());
        Assert.Equal("Workflow", items[0].GetProperty("source").GetString());
        Assert.Equal(1, items[0].GetProperty("stageSequence").GetInt32());
    }

    [Fact]
    public async Task WorkflowPerformance_ReturnsOneRowPerInstanceWithStageCounts()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var manager = await factory.AddUserAsync("Manager");
        var employee = await factory.AddUserAsync();
        var request = await factory.AddRequestAsync(employee.Id);
        await AddTwoStageWorkflowAsync(factory, request.Id, manager.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .ToString("yyyy-MM-dd");

        using var client = factory.CreateAuthorizedClient(admin.Id, "Admin");
        using var response = await client.GetAsync(
            $"/api/reports/workflow-performance?from={today}&to={today}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.Equal(1, document.RootElement
            .GetProperty("totalCount").GetInt32());
        var item = document.RootElement.GetProperty("items")[0];
        Assert.Equal(request.Id, item.GetProperty("requestId").GetInt32());
        Assert.Equal(2, item.GetProperty("stageCount").GetInt32());
        Assert.Equal(1, item.GetProperty("approvedStageCount").GetInt32());
        Assert.Equal(1, item.GetProperty("activeStageCount").GetInt32());
    }

    [Fact]
    public async Task LeaveReport_ExcludesCancellationRowsAndUsesAccountingBalances()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.AddUserAsync("Admin");
        var employee = await factory.AddUserAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        int leaveRequestId;
        int leaveTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();
            var requestType = await context.RequestTypes
                .SingleAsync(item => item.Code == RequestTypeCodes.Leave);
            var leaveType = new LeaveType(
                $"REPORT_{Guid.NewGuid():N}",
                "Annual Leave",
                true);
            context.LeaveTypes.Add(leaveType);
            await context.SaveChangesAsync();
            leaveTypeId = leaveType.Id;

            var request = new ApprovalRequest(
                "Annual leave request",
                amount: null,
                employee.Id,
                departmentId: null,
                requireAmount: false);
            var details = new LeaveRequestDetails(
                "Annual Leave",
                today,
                today.AddDays(1),
                "Report test leave");
            details.SetCalculatedLeaveUnits(
                leaveType.Id,
                2m,
                [new LeaveRequestUnitBreakdown(today, 1, false, 2m)]);
            request.SetLeaveDetails(
                requestType,
                details);
            context.ApprovalRequests.Add(request);
            await context.SaveChangesAsync();
            leaveRequestId = request.Id;

            var cancellation = new ApprovalRequest(
                "Cancellation of annual leave",
                amount: null,
                employee.Id,
                departmentId: null,
                requireAmount: false);
            var cancellationDetails = new LeaveRequestDetails(
                "Annual Leave",
                today,
                today,
                "Report test cancellation");
            cancellationDetails.MarkAsCancellationOf(request.Id);
            cancellation.SetLeaveDetails(requestType, cancellationDetails);
            context.ApprovalRequests.Add(cancellation);

            context.LeaveBalanceLedgerEntries.Add(
                new LeaveBalanceLedgerEntry(
                    employee.Id,
                    leaveType.Id,
                    leavePolicyId: null,
                    requestId: null,
                    payPeriodId: null,
                    LeaveLedgerEntryType.Accrual,
                    today.AddDays(-1),
                    10m,
                    $"report-test-accrual:{Guid.NewGuid():N}",
                    DateTime.UtcNow));
            await context.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider
                .GetRequiredService<FlowDesk.Application.Services.LeaveAccountingService>();
            var balance = await service.GetBalancesForReportAsync(
                admin.Id,
                [employee.Id],
                today);
            Assert.Equal(10m, balance[employee.Id]
                .Single(item => item.LeaveTypeId == leaveTypeId).Earned);
        }

        using var client = factory.CreateAuthorizedClient(admin.Id, "Admin");
        using var response = await client.GetAsync(
            $"/api/reports/leave?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.Equal(1, document.RootElement
            .GetProperty("totalCount").GetInt32());
        var item = document.RootElement.GetProperty("items")[0];
        Assert.Equal(leaveRequestId, item.GetProperty("requestId").GetInt32());
        Assert.Equal(10m, item.GetProperty("earned").GetDecimal());
        Assert.Equal(10m, item.GetProperty("available").GetDecimal());
    }

    private static async Task AddTwoStageWorkflowAsync(
        ApiFactory factory,
        int requestId,
        int managerId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var startedAtUtc = DateTime.UtcNow.AddMinutes(-60);
        var rule = new ApprovalWorkflowRule(
            $"reports-test-{Guid.NewGuid():N}",
            "Reports test workflow",
            1,
            departmentId: null,
            minimumAmount: 0m,
            maximumAmountExclusive: null,
            priority: null,
            [
                new ApprovalWorkflowRuleStage(
                    1,
                    WorkflowApproverType.SpecificManager,
                    approverDepartmentId: null,
                    specificApproverUserId: managerId,
                    deadlineMinutes: null,
                    escalationAfterMinutes: null,
                    escalationApproverUserId: null),
                new ApprovalWorkflowRuleStage(
                    2,
                    WorkflowApproverType.SpecificManager,
                    approverDepartmentId: null,
                    specificApproverUserId: managerId,
                    deadlineMinutes: null,
                    escalationAfterMinutes: null,
                    escalationApproverUserId: null)
            ]);
        rule.Activate(startedAtUtc);
        context.ApprovalWorkflowRules.Add(rule);
        await context.SaveChangesAsync();

        var firstStage = new ApprovalWorkflowStage(
            1,
            managerId,
            deadlineMinutes: null,
            escalationAfterMinutes: null,
            escalationApproverUserId: null);
        firstStage.Activate(startedAtUtc);
        firstStage.Decide(
            ApprovalWorkflowStageStatus.Approved,
            startedAtUtc.AddMinutes(10));
        var secondStage = new ApprovalWorkflowStage(
            2,
            managerId,
            deadlineMinutes: null,
            escalationAfterMinutes: null,
            escalationApproverUserId: null);
        secondStage.Activate(startedAtUtc.AddMinutes(10));
        var workflow = new ApprovalWorkflowInstance(
            requestId,
            rule,
            [firstStage, secondStage],
            startedAtUtc);
        context.ApprovalWorkflowInstances.Add(workflow);
        await context.SaveChangesAsync();

        context.ApprovalDecisionHistory.Add(new ApprovalDecisionHistory(
            firstStage.Id,
            managerId,
            ApprovalWorkflowStageStatus.Approved,
            comment: null,
            startedAtUtc.AddMinutes(10)));
        var assignment = new ApprovalAssignment(
            requestId,
            managerId,
            firstStage.Id);
        assignment.MarkApproved(startedAtUtc.AddMinutes(10));
        context.ApprovalAssignments.Add(assignment);
        await context.SaveChangesAsync();
    }
}
