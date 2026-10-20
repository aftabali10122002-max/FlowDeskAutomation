namespace FlowDisk.Api.Security;

public static class RequestPolicies
{
    public const string ReadAllRequests = "ReadAllRequests";
    public const string ReviewLegacyRequests = "ReviewLegacyRequests";
    public const string ManageApprovalAssignments = "ManageApprovalAssignments";
    public const string LoginRateLimit = "LoginRateLimit";
    public const string RegistrationRateLimit = "RegistrationRateLimit";
}
