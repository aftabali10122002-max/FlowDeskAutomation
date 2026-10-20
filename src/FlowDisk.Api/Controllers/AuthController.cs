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

    public AuthController(AuthenticationService authService)
    {
        _authService = authService;
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
public IActionResult GetCurrentUser()
{
    var userId = User.FindFirstValue(
        ClaimTypes.NameIdentifier);

    var fullName = User.FindFirstValue(
        ClaimTypes.Name);

    var email = User.FindFirstValue(
        ClaimTypes.Email);

    var role = User.FindFirstValue(
        ClaimTypes.Role);

    return Ok(new
    {
        UserId = userId,
        FullName = fullName,
        Email = email,
        Role = role
    });
}
}