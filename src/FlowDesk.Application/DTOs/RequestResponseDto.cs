using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.DTOs;
public class RequestResponseDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal? Amount { get; set; }

    public int? RequestTypeId { get; set; }
    public string? RequestTypeCode { get; set; }
    public string? RequestTypeName { get; set; }
    public object? RequestDetails { get; set; }
    public RequestPriority? Priority { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public int? CreatedByUserId { get; set; }

    public string? CreatedByName { get; set; }

    public string? CreatedByEmail { get; set; }

    public string? SubmittedBy { get; set; }

    public int? DepartmentId { get; set; }

    public string? DepartmentName { get; set; }

    public string? Department { get; set; }

    public List<ApprovalAssignmentResponseDto> Assignments { get; set; } = [];
}