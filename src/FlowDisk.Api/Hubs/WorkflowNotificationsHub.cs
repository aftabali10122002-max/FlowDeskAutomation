using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FlowDisk.Api.Hubs;

[Authorize]
public sealed class WorkflowNotificationsHub : Hub
{
}
