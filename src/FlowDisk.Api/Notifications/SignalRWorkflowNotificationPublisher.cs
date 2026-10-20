using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDisk.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FlowDisk.Api.Notifications;

public sealed class SignalRWorkflowNotificationPublisher(
    IHubContext<WorkflowNotificationsHub> hub)
    : IWorkflowNotificationPublisher
{
    public Task PublishAsync(WorkflowNotification notification)
    {
        return hub.Clients
            .User(notification.RecipientUserId.ToString())
            .SendAsync(
                "notificationReceived",
                new
                {
                    notification.Id,
                    notification.RequestId,
                    Type = notification.Type.ToString(),
                    notification.Message,
                    notification.CreatedAtUtc,
                    notification.ReadAtUtc
                });
    }
}
