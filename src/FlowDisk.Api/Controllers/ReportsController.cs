using System.Security.Claims;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Services;
using FlowDisk.Api.Reports;
using FlowDisk.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowDisk.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Manager,Employee")]
[Route("api/reports")]
public sealed class ReportsController(
    ReportsService reportsService,
    ReportsExportService reportsExportService,
    IAuthorizationService authorizationService) : ControllerBase
{
    [HttpGet("requests")]
    [ProducesResponseType(typeof(ReportPage<RequestReportRow>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRequests(
        [FromQuery] ReportQueryInput input)
    {
        if (!TryGetActor(out var userId, out var role) ||
            !await CanReadReportsAsync(role))
        {
            return Forbid();
        }

        return Ok(await reportsService.GetRequestsAsync(
            userId,
            role,
            input));
    }

    [HttpGet("approval-history")]
    [ProducesResponseType(typeof(ReportPage<ApprovalHistoryReportRow>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApprovalHistory(
        [FromQuery] ReportQueryInput input)
    {
        if (!TryGetActor(out var userId, out var role) ||
            !await CanReadReportsAsync(role))
        {
            return Forbid();
        }

        return Ok(await reportsService.GetApprovalHistoryAsync(
            userId,
            role,
            input));
    }

    [HttpGet("workflow-performance")]
    [ProducesResponseType(typeof(ReportPage<WorkflowPerformanceReportRow>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetWorkflowPerformance(
        [FromQuery] ReportQueryInput input)
    {
        if (!TryGetActor(out var userId, out var role) ||
            !await CanReadReportsAsync(role))
        {
            return Forbid();
        }

        return Ok(await reportsService.GetWorkflowPerformanceAsync(
            userId,
            role,
            input));
    }

    [HttpGet("leave")]
    [ProducesResponseType(typeof(ReportPage<LeaveReportRow>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetLeaveReport(
        [FromQuery] ReportQueryInput input)
    {
        if (!TryGetActor(out var userId, out var role) ||
            !await CanReadReportsAsync(role))
        {
            return Forbid();
        }

        return Ok(await reportsService.GetLeaveReportAsync(
            userId,
            role,
            input));
    }

    [HttpGet("filter-options")]
    [ProducesResponseType(typeof(ReportFilterOptions), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetFilterOptions()
    {
        if (!TryGetActor(out var userId, out var role) ||
            !await CanReadReportsAsync(role))
        {
            return Forbid();
        }

        return Ok(await reportsService.GetFilterOptionsAsync(userId, role));
    }

    [HttpGet("requests/export")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ExportRequests(
        [FromQuery] string? format,
        [FromQuery] ReportQueryInput input,
        CancellationToken cancellationToken) =>
        ExportAsync("requests", format, input, cancellationToken);

    [HttpGet("approval-history/export")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ExportApprovalHistory(
        [FromQuery] string? format,
        [FromQuery] ReportQueryInput input,
        CancellationToken cancellationToken) =>
        ExportAsync("approval-history", format, input, cancellationToken);

    [HttpGet("workflow-performance/export")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ExportWorkflowPerformance(
        [FromQuery] string? format,
        [FromQuery] ReportQueryInput input,
        CancellationToken cancellationToken) =>
        ExportAsync("workflow-performance", format, input, cancellationToken);

    [HttpGet("leave/export")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ExportLeave(
        [FromQuery] string? format,
        [FromQuery] ReportQueryInput input,
        CancellationToken cancellationToken) =>
        ExportAsync("leave", format, input, cancellationToken);

    private async Task<IActionResult> ExportAsync(
        string report,
        string? format,
        ReportQueryInput input,
        CancellationToken cancellationToken)
    {
        if (format is null ||
            (!string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase)))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unsupported export format",
                detail: "Format must be either xlsx or pdf.");
        }

        if (!TryGetActor(out var userId, out var role) ||
            !await CanReadReportsAsync(role))
        {
            return Forbid();
        }

        try
        {
            var file = await reportsExportService.CreateAsync(
                report,
                format,
                userId,
                role,
                input,
                cancellationToken);
            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (ReportExportLimitExceededException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Report export exceeds the configured limit",
                detail: exception.Message);
        }
        catch (ReportExportDataChangedException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Report data changed during export",
                detail: exception.Message);
        }
    }

    private async Task<bool> CanReadReportsAsync(string role)
    {
        return role != "Admin" ||
            (await authorizationService.AuthorizeAsync(
                User,
                RequestPolicies.ReadAllRequests)).Succeeded;
    }

    private bool TryGetActor(out int userId, out string role)
    {
        role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return int.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out userId) &&
            userId > 0 &&
            role is "Admin" or "Manager" or "Employee";
    }
}
