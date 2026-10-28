using System.ComponentModel.DataAnnotations;
using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.DTOs;
public class CreateRequestDto
{
    public string? Title { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    public string? RequestTypeCode { get; set; }

    public decimal? Amount { get; set; }
    public RequestPriority? Priority { get; set; }

    public MaterialPurchaseRequestInputDto? MaterialPurchase { get; set; }
    public LeaveRequestInputDto? Leave { get; set; }
    public ExpenseReimbursementRequestInputDto? ExpenseReimbursement
        { get; set; }
    public ItAccessRequestInputDto? ItAccess { get; set; }
    public TravelRequestInputDto? Travel { get; set; }
    public WorkFromHomeRequestInputDto? WorkFromHome { get; set; }
}

public sealed class MaterialPurchaseRequestInputDto
{
    public string ItemName { get; set; } = string.Empty;
    public int? Quantity { get; set; }
    public string BusinessJustification { get; set; } = string.Empty;
}

public sealed class LeaveRequestInputDto
{
    public string LeaveType { get; set; } = string.Empty;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public LeaveDayPortion StartDayPortion { get; set; } =
        LeaveDayPortion.FullDay;
    public LeaveDayPortion EndDayPortion { get; set; } =
        LeaveDayPortion.FullDay;
}

public sealed class ExpenseReimbursementRequestInputDto
{
    public string Category { get; set; } = string.Empty;
    public DateOnly? ExpenseDate { get; set; }
    public string BusinessJustification { get; set; } = string.Empty;
}

public sealed class ItAccessRequestInputDto
{
    public string SystemApplication { get; set; } = string.Empty;
    public string RequestedAccessLevel { get; set; } = string.Empty;
    public int? DurationDays { get; set; }
    public string BusinessJustification { get; set; } = string.Empty;
}

public sealed class TravelRequestInputDto
{
    public string Destination { get; set; } = string.Empty;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string BusinessPurpose { get; set; } = string.Empty;
}

public sealed class WorkFromHomeRequestInputDto
{
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}