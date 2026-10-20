using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public interface IWorkflowNotificationPublisher
{
    Task PublishAsync(WorkflowNotification notification);
}
