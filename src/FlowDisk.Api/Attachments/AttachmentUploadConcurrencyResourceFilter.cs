using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FlowDisk.Api.Attachments;

public sealed class AttachmentUploadConcurrencyResourceFilter(
    AttachmentUploadConcurrencyLimiter uploadLimiter) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        var claimValue = context.HttpContext.User.FindFirstValue(
            ClaimTypes.NameIdentifier);
        if (!int.TryParse(claimValue, out var userId) || userId <= 0)
        {
            await next();
            return;
        }

        using var uploadLease = uploadLimiter.TryAcquire(userId);
        if (uploadLease is null)
        {
            context.Result = new ObjectResult(new
            {
                message = "Too many attachment uploads are in progress."
            })
            {
                StatusCode = StatusCodes.Status429TooManyRequests
            };
            return;
        }

        await next();
    }
}
