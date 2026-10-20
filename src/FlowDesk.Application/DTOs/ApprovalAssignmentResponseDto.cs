namespace FlowDesk.Application.DTOs;

public class ApprovalAssignmentResponseDto
{
    public long Id { get; set; }

    public string ApproverName { get; set; } = string.Empty;

    public string? ApproverEmail { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime AssignedAtUtc { get; set; }

    public DateTime? DecidedAtUtc { get; set; }
}
