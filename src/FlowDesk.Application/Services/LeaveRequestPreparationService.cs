using FlowDesk.Application.Errors;
using FlowDesk.Application.Interfaces;
using FlowDesk.Application.DTOs;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Services;

public sealed class LeaveRequestPreparationService(
    ILeaveManagementRepository leaveRepository) :
    ILeaveRequestPreparationService
{
    public async Task PrepareAsync(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var details = request.LeaveDetails
            ?? throw new LeaveInvalidInputException(
                "Leave request details are required.");
        if (request.CreatedByUserId is not int employeeUserId)
        {
            throw new LeaveInvalidInputException(
                "A leave request owner is required.");
        }

        var leaveTypeId = await leaveRepository.GetEnabledLeaveTypeIdAsync(
            details.LeaveType);
        if (leaveTypeId is not int enabledLeaveTypeId)
        {
            throw new LeaveConfigurationException(
                "The requested leave type is not enabled.");
        }

        var resolutionData =
            await leaveRepository.GetPolicyResolutionDataAsync(
                employeeUserId,
                details.StartDate,
                details.EndDate)
            ?? throw new LeaveNotFoundException("Employee");

        var breakdown = Calculate(
            resolutionData,
            enabledLeaveTypeId,
            details.StartDate,
            details.EndDate,
            details.StartDayPortion,
            details.EndDayPortion);
        details.SetCalculatedLeaveUnits(
            enabledLeaveTypeId,
            LeaveAccrualCalculator.RoundUnits(
                breakdown.Sum(item => item.Units)),
            breakdown);
    }

    public async Task<LeaveRequestPreview> PreviewAsync(
        int employeeUserId,
        LeaveRequestPreviewInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (employeeUserId <= 0 ||
            !Enum.IsDefined(input.StartDayPortion) ||
            !Enum.IsDefined(input.EndDayPortion))
        {
            throw new LeaveInvalidInputException(
                "The leave preview values are invalid.");
        }

        var leaveTypeId = await leaveRepository.GetEnabledLeaveTypeIdAsync(
            input.LeaveType);
        if (leaveTypeId is not int enabledLeaveTypeId)
        {
            throw new LeaveConfigurationException(
                "The requested leave type is not enabled.");
        }

        var resolutionData =
            await leaveRepository.GetPolicyResolutionDataAsync(
                employeeUserId,
                input.StartDate,
                input.EndDate)
            ?? throw new LeaveNotFoundException("Employee");
        var breakdown = Calculate(
            resolutionData,
            enabledLeaveTypeId,
            input.StartDate,
            input.EndDate,
            input.StartDayPortion,
            input.EndDayPortion);
        return new LeaveRequestPreview(
            LeaveAccrualCalculator.RoundUnits(
                breakdown.Sum(day => day.Units)),
            breakdown.Select(day => new LeaveRequestPreviewDay(
                day.Date,
                day.Units,
                day.PolicyId,
                resolutionData.Policies
                    .Single(policy => policy.Id == day.PolicyId)
                    .Version))
                .ToList());
    }

    private static IReadOnlyList<LeaveRequestUnitBreakdown> Calculate(
        LeavePolicyResolutionData resolutionData,
        int leaveTypeId,
        DateOnly startDate,
        DateOnly endDate,
        FlowDesk.Domain.Enums.LeaveDayPortion startDayPortion,
        FlowDesk.Domain.Enums.LeaveDayPortion endDayPortion) =>
        LeaveDayCalculator.Calculate(
                startDate,
                endDate,
                startDayPortion,
                endDayPortion,
                date => LeavePolicyResolver.Resolve(
                    resolutionData.Employee,
                    resolutionData.EmploymentProfiles,
                    resolutionData.Assignments,
                    resolutionData.Policies,
                    leaveTypeId,
                    date),
                resolutionData.WorkingDaysBySchedule,
                resolutionData.HolidaysByCalendar)
            .Select(charge => new LeaveRequestUnitBreakdown(
                charge.Date,
                charge.Policy.Id,
                charge.Policy.AllowsNegativeBalance,
                charge.Units))
            .ToList();
}
