using FlowDesk.Application.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowDisk.Api.Exceptions;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            "Request failed with {ExceptionType}; TraceId: {TraceId}",
            exception.GetType().Name,
            httpContext.TraceIdentifier);

        var statusCode = exception switch
        {
            LeaveUnauthorizedException => StatusCodes.Status403Forbidden,
            LeaveNotFoundException => StatusCodes.Status404NotFound,
            LeaveInvalidInputException => StatusCodes.Status400BadRequest,
            LeaveOverlapException => StatusCodes.Status409Conflict,
            LeaveConfigurationException => StatusCodes.Status409Conflict,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            ArgumentException => StatusCodes.Status400BadRequest,
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            InvalidOperationException => StatusCodes.Status409Conflict,
            DbUpdateException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        httpContext.Response.StatusCode = statusCode;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode == 500
                ? "Internal Server Error"
                : exception is LeaveApplicationException
                    ? "Leave operation failed"
                    : "Request failed",
            Detail = exception is LeaveApplicationException or ArgumentException
                ? exception.Message
                : statusCode switch
                {
                    StatusCodes.Status403Forbidden =>
                        "The authenticated user is not authorized.",
                    StatusCodes.Status409Conflict =>
                        "The request conflicts with the current state.",
                    _ => "An unexpected error occurred."
                }
        };
        if (exception is LeaveApplicationException leaveException)
        {
            problem.Extensions["code"] = leaveException.Code;
        }

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            cancellationToken);

        return true;
    }
}