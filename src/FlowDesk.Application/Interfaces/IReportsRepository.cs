using FlowDesk.Application.DTOs;

namespace FlowDesk.Application.Interfaces;

public interface IReportsRepository
{
    Task<ReportPage<RequestReportRow>> GetRequestsAsync(
        ReportActor actor,
        ReportFilter filter);

    Task<ReportPage<ApprovalHistoryReportRow>> GetApprovalHistoryAsync(
        ReportActor actor,
        ReportFilter filter);

    Task<ReportPage<WorkflowPerformanceReportRow>> GetWorkflowPerformanceAsync(
        ReportActor actor,
        ReportFilter filter);

    Task<ReportPage<LeaveReportRow>> GetLeaveReportAsync(
        ReportActor actor,
        ReportFilter filter);

    Task<ReportFilterOptions> GetFilterOptionsAsync(ReportActor actor);
}
