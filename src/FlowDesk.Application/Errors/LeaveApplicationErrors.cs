namespace FlowDesk.Application.Errors;

public abstract class LeaveApplicationException(
    string code,
    string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class LeaveUnauthorizedException()
    : LeaveApplicationException(
        "leave_unauthorized",
        "The requested leave operation is not authorized.");

public sealed class LeaveNotFoundException(string resource)
    : LeaveApplicationException(
        "leave_not_found",
        $"{resource} was not found.");

public class LeaveConfigurationException(string message)
    : LeaveApplicationException("leave_configuration_error", message);

public sealed class LeaveOverlapException(string message)
    : LeaveApplicationException("leave_effective_date_overlap", message);

public sealed class LeaveInvalidInputException(string message)
    : LeaveApplicationException("leave_invalid_input", message);
