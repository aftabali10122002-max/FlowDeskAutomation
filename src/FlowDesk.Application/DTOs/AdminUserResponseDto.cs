namespace FlowDesk.Application.DTOs;

public class AdminUserResponseDto
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int? DepartmentId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
