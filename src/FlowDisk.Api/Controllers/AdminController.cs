using System.Security.Claims;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Services;
using FlowDesk.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowDisk.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoles.Admin)]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly AdminManagementService _service;
    private readonly RequestTypeService _requestTypeService;

    public AdminController(
        AdminManagementService service,
        RequestTypeService requestTypeService)
    {
        _service = service;
        _requestTypeService = requestTypeService;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var adminUserId = GetAuthenticatedUserId();
        if (adminUserId == null)
        {
            return Forbid();
        }

        var users = await _service.GetUsersAsync(adminUserId.Value);
        return Ok(users.Select(MapUser).ToList());
    }

    [HttpGet("users/{id:int}")]
    public async Task<IActionResult> GetUser(int id)
    {
        var adminUserId = GetAuthenticatedUserId();
        if (adminUserId == null)
        {
            return Forbid();
        }

        var user = await _service.GetUserAsync(adminUserId.Value, id);
        return user == null
            ? NotFound(new { message = "User not found." })
            : Ok(MapUser(user));
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser(CreateAdminUserDto dto)
    {
        var adminUserId = GetAuthenticatedUserId();
        if (adminUserId == null)
        {
            return Forbid();
        }

        var user = await _service.CreateUserAsync(
            adminUserId.Value,
            dto.FullName,
            dto.Email,
            dto.Password,
            dto.Role,
            dto.DepartmentId);

        return CreatedAtAction(
            nameof(GetUser),
            new { id = user.Id },
            MapUser(user));
    }

    [HttpPatch("users/{id:int}/role")]
    public async Task<IActionResult> UpdateUserRole(
        int id,
        UpdateUserRoleDto dto)
    {
        var adminUserId = GetAuthenticatedUserId();
        if (adminUserId == null)
        {
            return Forbid();
        }

        var user = await _service.UpdateUserRoleAsync(
            adminUserId.Value,
            id,
            dto.Role);

        return user == null
            ? NotFound(new { message = "User not found." })
            : Ok(MapUser(user));
    }

    [HttpPatch("users/{id:int}/department")]
    public async Task<IActionResult> UpdateUserDepartment(
        int id,
        UpdateUserDepartmentDto dto)
    {
        var adminUserId = GetAuthenticatedUserId();
        if (adminUserId == null)
        {
            return Forbid();
        }

        var user = await _service.UpdateUserDepartmentAsync(
            adminUserId.Value,
            id,
            dto.DepartmentId);

        return user == null
            ? NotFound(new { message = "User not found." })
            : Ok(MapUser(user));
    }

    [HttpPatch("users/{id:int}/active")]
    public async Task<IActionResult> UpdateUserActive(
        int id,
        UpdateUserActiveDto dto)
    {
        var adminUserId = GetAuthenticatedUserId();
        if (adminUserId == null)
        {
            return Forbid();
        }

        var user = await _service.UpdateUserActiveAsync(
            adminUserId.Value,
            id,
            dto.IsActive);
        return user == null
            ? NotFound(new { message = "User not found." })
            : Ok(MapUser(user));
    }

    [HttpGet("departments")]
    public async Task<IActionResult> GetDepartments()
    {
        var adminUserId = GetAuthenticatedUserId();
        if (adminUserId == null)
        {
            return Forbid();
        }

        var departments =
            await _service.GetDepartmentsAsync(adminUserId.Value);
        return Ok(departments.Select(MapDepartment).ToList());
    }

    [HttpGet("request-types")]
    public async Task<IActionResult> GetRequestTypes()
    {
        var adminUserId = GetAuthenticatedUserId();
        if (adminUserId == null)
        {
            return Forbid();
        }

        return Ok(await _requestTypeService.GetAllForAdminAsync(
            adminUserId.Value));
    }

    [HttpPatch("request-types/{id:int}/enabled")]
    public async Task<IActionResult> SetRequestTypeEnabled(
        int id,
        SetRequestTypeEnabledDto dto)
    {
        var adminUserId = GetAuthenticatedUserId();
        if (adminUserId == null)
        {
            return Forbid();
        }

        var requestType = await _requestTypeService.SetEnabledAsync(
            adminUserId.Value,
            id,
            dto.IsEnabled);
        return requestType is null
            ? NotFound(new { message = "Request type not found." })
            : Ok(requestType);
    }

    [HttpPost("departments")]
    public async Task<IActionResult> CreateDepartment(CreateDepartmentDto dto)
    {
        var adminUserId = GetAuthenticatedUserId();
        if (adminUserId == null)
        {
            return Forbid();
        }

        var department = await _service.CreateDepartmentAsync(
            adminUserId.Value,
            dto.Name,
            dto.Code);

        return StatusCode(
            StatusCodes.Status201Created,
            MapDepartment(department));
    }

    private int? GetAuthenticatedUserId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claimValue, out var userId) && userId > 0
            ? userId
            : null;
    }

    private static AdminUserResponseDto MapUser(User user)
    {
        return new AdminUserResponseDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role,
            IsActive = user.IsActive,
            DepartmentId = user.DepartmentId,
            CreatedAtUtc = user.CreatedAtUtc,
            HasProfileImage = user.ProfileImageStorageKey is not null,
            ProfileImageUrl = $"/api/profile/users/{user.Id}/photo"
        };
    }

    private static DepartmentResponseDto MapDepartment(
        Department department)
    {
        return new DepartmentResponseDto
        {
            Id = department.Id,
            Name = department.Name,
            Code = department.Code,
            IsActive = department.IsActive,
            CreatedAtUtc = department.CreatedAtUtc
        };
    }
}
