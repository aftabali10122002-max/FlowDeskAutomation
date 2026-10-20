namespace FlowDesk.Application.DTOs;

public class CreateAdminUserDto
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public int DepartmentId { get; set; }
}

