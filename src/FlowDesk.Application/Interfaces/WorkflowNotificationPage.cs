using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public sealed record WorkflowNotificationPage(
    IReadOnlyList<WorkflowNotification> Items,
    int TotalCount,
    int UnreadCount,
    int Page,
    int PageSize);
