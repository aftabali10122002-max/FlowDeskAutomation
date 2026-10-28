using System.ComponentModel.DataAnnotations.Schema;
using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public sealed class MaterialPurchaseRequestDetails
{
    public int RequestId { get; private set; }
    public ApprovalRequest Request { get; private set; } = null!;
    public string ItemName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public string BusinessJustification { get; private set; } = string.Empty;

    private MaterialPurchaseRequestDetails() { }

    public MaterialPurchaseRequestDetails(
        string itemName,
        int quantity,
        string businessJustification)
    {
        ItemName = RequireText(itemName, 200, nameof(itemName));
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Quantity must be greater than zero.");
        }

        Quantity = quantity;
        BusinessJustification = RequireText(
            businessJustification,
            2000,
            nameof(businessJustification));
    }

    internal void AttachTo(ApprovalRequest request)
    {
        Request = request;
        RequestId = request.Id;
    }

    internal static string RequireText(
        string value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Trim().Length > maximumLength)
        {
            throw new ArgumentException(
                $"A value is required and cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return value.Trim();
    }
}

public sealed class LeaveRequestDetails
{
    public int RequestId { get; private set; }
    public ApprovalRequest Request { get; private set; } = null!;
    public string LeaveType { get; private set; } = string.Empty;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public int TotalLeaveDays { get; private set; }
    public LeaveDayPortion StartDayPortion { get; private set; } =
        LeaveDayPortion.FullDay;
    public LeaveDayPortion EndDayPortion { get; private set; } =
        LeaveDayPortion.FullDay;
    public int? CancellationOfRequestId { get; private set; }
    public int? ConfiguredLeaveTypeId { get; private set; }
    public decimal? CalculatedLeaveUnits { get; private set; }
    [NotMapped]
    public IReadOnlyList<LeaveRequestUnitBreakdown> UnitBreakdown { get; private set; }
        = [];

    private LeaveRequestDetails() { }

    public LeaveRequestDetails(
        string leaveType,
        DateOnly startDate,
        DateOnly endDate,
        string reason,
        LeaveDayPortion startDayPortion = LeaveDayPortion.FullDay,
        LeaveDayPortion endDayPortion = LeaveDayPortion.FullDay)
    {
        LeaveType = MaterialPurchaseRequestDetails.RequireText(
            leaveType,
            50,
            nameof(leaveType));
        ValidateDateRange(startDate, endDate);
        if (!Enum.IsDefined(startDayPortion) ||
            !Enum.IsDefined(endDayPortion))
        {
            throw new ArgumentOutOfRangeException(nameof(startDayPortion));
        }

        if (startDate == endDate &&
            startDayPortion == LeaveDayPortion.HalfDay &&
            endDayPortion == LeaveDayPortion.HalfDay)
        {
            throw new ArgumentException(
                "A single-day leave request can specify only one half-day portion.");
        }

        StartDate = startDate;
        EndDate = endDate;
        Reason = MaterialPurchaseRequestDetails.RequireText(
            reason,
            2000,
            nameof(reason));
        TotalLeaveDays = endDate.DayNumber - startDate.DayNumber + 1;
        StartDayPortion = startDayPortion;
        EndDayPortion = endDayPortion;
    }

    public void SetCalculatedLeaveUnits(
        int leaveTypeId,
        decimal units,
        IReadOnlyList<LeaveRequestUnitBreakdown> unitBreakdown)
    {
        ArgumentNullException.ThrowIfNull(unitBreakdown);
        if (leaveTypeId <= 0 ||
            units <= 0 ||
            units != decimal.Round(units, 2, MidpointRounding.AwayFromZero))
        {
            throw new ArgumentOutOfRangeException(nameof(units));
        }

        if (unitBreakdown.Count == 0 ||
            unitBreakdown.Any(item =>
            item.Date < StartDate ||
            item.Date > EndDate ||
            item.PolicyId <= 0 ||
            item.Units <= 0 ||
            item.Units != decimal.Round(
                item.Units,
                2,
                MidpointRounding.AwayFromZero)) ||
            unitBreakdown.Sum(item => item.Units) != units)
        {
            throw new ArgumentException(
            "The leave unit breakdown is invalid or does not match the total.",
            nameof(unitBreakdown));
        }

        ConfiguredLeaveTypeId = leaveTypeId;
        CalculatedLeaveUnits = units;
        UnitBreakdown = unitBreakdown.ToList();
    }

    public void MarkAsCancellationOf(int originalRequestId)
    {
        if (originalRequestId <= 0 || originalRequestId == RequestId)
        {
            throw new ArgumentOutOfRangeException(nameof(originalRequestId));
        }

        CancellationOfRequestId = originalRequestId;
        ConfiguredLeaveTypeId = null;
        CalculatedLeaveUnits = null;
        UnitBreakdown = [];
    }

    internal void AttachTo(ApprovalRequest request)
    {
        Request = request;
        RequestId = request.Id;
    }

    internal static void ValidateDateRange(
        DateOnly startDate,
        DateOnly endDate)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException(
                "The end date cannot be before the start date.");
        }
    }
}

public sealed record LeaveRequestUnitBreakdown(
    DateOnly Date,
    int PolicyId,
    bool AllowsNegativeBalance,
    decimal Units);

public sealed class ExpenseReimbursementRequestDetails
{
    public int RequestId { get; private set; }
    public ApprovalRequest Request { get; private set; } = null!;
    public string Category { get; private set; } = string.Empty;
    public DateOnly ExpenseDate { get; private set; }
    public string BusinessJustification { get; private set; } = string.Empty;

    private ExpenseReimbursementRequestDetails() { }

    public ExpenseReimbursementRequestDetails(
        string category,
        DateOnly expenseDate,
        string businessJustification)
    {
        Category = MaterialPurchaseRequestDetails.RequireText(
            category,
            100,
            nameof(category));
        ExpenseDate = expenseDate;
        BusinessJustification = MaterialPurchaseRequestDetails.RequireText(
            businessJustification,
            2000,
            nameof(businessJustification));
    }

    internal void AttachTo(ApprovalRequest request)
    {
        Request = request;
        RequestId = request.Id;
    }
}

public sealed class ItAccessRequestDetails
{
    public int RequestId { get; private set; }
    public ApprovalRequest Request { get; private set; } = null!;
    public string SystemApplication { get; private set; } = string.Empty;
    public string RequestedAccessLevel { get; private set; } = string.Empty;
    public int? DurationDays { get; private set; }
    public string BusinessJustification { get; private set; } = string.Empty;

    private ItAccessRequestDetails() { }

    public ItAccessRequestDetails(
        string systemApplication,
        string requestedAccessLevel,
        int? durationDays,
        string businessJustification)
    {
        SystemApplication = MaterialPurchaseRequestDetails.RequireText(
            systemApplication,
            150,
            nameof(systemApplication));
        RequestedAccessLevel = MaterialPurchaseRequestDetails.RequireText(
            requestedAccessLevel,
            100,
            nameof(requestedAccessLevel));
        if (durationDays is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationDays),
                "Duration must be greater than zero when supplied.");
        }

        DurationDays = durationDays;
        BusinessJustification = MaterialPurchaseRequestDetails.RequireText(
            businessJustification,
            2000,
            nameof(businessJustification));
    }

    internal void AttachTo(ApprovalRequest request)
    {
        Request = request;
        RequestId = request.Id;
    }
}

public sealed class TravelRequestDetails
{
    public int RequestId { get; private set; }
    public ApprovalRequest Request { get; private set; } = null!;
    public string Destination { get; private set; } = string.Empty;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string BusinessPurpose { get; private set; } = string.Empty;

    private TravelRequestDetails() { }

    public TravelRequestDetails(
        string destination,
        DateOnly startDate,
        DateOnly endDate,
        string businessPurpose)
    {
        Destination = MaterialPurchaseRequestDetails.RequireText(
            destination,
            200,
            nameof(destination));
        LeaveRequestDetails.ValidateDateRange(startDate, endDate);
        StartDate = startDate;
        EndDate = endDate;
        BusinessPurpose = MaterialPurchaseRequestDetails.RequireText(
            businessPurpose,
            2000,
            nameof(businessPurpose));
    }

    internal void AttachTo(ApprovalRequest request)
    {
        Request = request;
        RequestId = request.Id;
    }
}

public sealed class WorkFromHomeRequestDetails
{
    public int RequestId { get; private set; }
    public ApprovalRequest Request { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string Reason { get; private set; } = string.Empty;

    private WorkFromHomeRequestDetails() { }

    public WorkFromHomeRequestDetails(
        DateOnly startDate,
        DateOnly endDate,
        string reason)
    {
        LeaveRequestDetails.ValidateDateRange(startDate, endDate);
        StartDate = startDate;
        EndDate = endDate;
        Reason = MaterialPurchaseRequestDetails.RequireText(
            reason,
            2000,
            nameof(reason));
    }

    internal void AttachTo(ApprovalRequest request)
    {
        Request = request;
        RequestId = request.Id;
    }
}
