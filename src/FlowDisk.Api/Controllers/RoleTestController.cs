using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowDisk.Api.Controllers;

[ApiController]
[Route("api/role-test")]
public class RoleTestController : ControllerBase
{
    // Sirf Employee role
    [Authorize(Roles = "Employee")]
    [HttpGet("employee")]
    public IActionResult EmployeeOnly()
    {
        return Ok(new
        {
            message = "Employee access granted."
        });
    }

    // Sirf Admin role
    [Authorize(Roles = "Admin")]
    [HttpGet("admin")]
    public IActionResult AdminOnly()
    {
        return Ok(new
        {
            message = "Admin access granted."
        });
    }

    // Sirf Manager role
    [Authorize(Roles = "Manager")]
    [HttpGet("manager")]
    public IActionResult ManagerOnly()
    {
        return Ok(new
        {
            message = "Manager access granted."
        });
    }
}