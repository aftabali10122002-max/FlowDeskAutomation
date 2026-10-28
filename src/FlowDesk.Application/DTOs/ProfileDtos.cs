using System.ComponentModel.DataAnnotations;

namespace FlowDesk.Application.DTOs;

public sealed class UserProfileResponseDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string? PhoneNumber { get; set; }
    public bool HasProfileImage { get; set; }
    public string ProfileImageUrl { get; set; } = string.Empty;
}

public sealed class UpdateMyProfileDto
{
    [Required]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(32)]
    public string? PhoneNumber { get; set; }
}
