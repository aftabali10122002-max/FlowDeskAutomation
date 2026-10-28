using System.Security.Claims;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Services;
using FlowDisk.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FlowDisk.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthenticationService _authService;
    private readonly ProfileService _profileService;

    public AuthController(
        AuthenticationService authService,
        ProfileService profileService)
    {
        _authService = authService;
        _profileService = profileService;
    }

    [HttpPost("register")]
    [EnableRateLimiting(RequestPolicies.RegistrationRateLimit)]
    public async Task<IActionResult> Register(RegisterUserRequest dto)
    {
        try
        {
            var user = await _authService.RegisterAsync(dto);

            return StatusCode(
                StatusCodes.Status201Created,
                new
                {
                    user.Id,
                    user.FullName,
                    user.Email,
                    user.Role,
                    user.CreatedAtUtc
                });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    [EnableRateLimiting(RequestPolicies.LoginRateLimit)]
    public async Task<IActionResult> Login(LoginUserRequest dto)
    {
        try
        {
            var response = await _authService.LoginAsync(dto);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new
            {
                message = "Invalid email or password."
            });
        }
    }



[Authorize]
[HttpGet("me")]
public async Task<IActionResult> GetCurrentUser()
{
    var userId = User.FindFirstValue(
        ClaimTypes.NameIdentifier);
    if (!int.TryParse(userId, out var parsedUserId) || parsedUserId <= 0)
    {
        return Forbid();
    }

    var profile = await _profileService.GetMyProfileAsync(parsedUserId);
    return profile is null ? Forbid() : Ok(profile);
}
}