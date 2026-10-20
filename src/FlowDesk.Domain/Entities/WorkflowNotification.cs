using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public sealed class WorkflowNotification
{
    public long Id { get; private set; }
    public int RecipientUserId { get; private set; }
    public User RecipientUser { get; private set; } = null!;
    public int? RequestId { get; private set; }
    public ApprovalRequest? Request { get; private set; }
    public long? EscalationId { get; private set; }
    public ApprovalWorkflowEscalation? Escalation { get; private set; }
    public WorkflowNotificationType Type { get; private set; }
    public string? DeduplicationKey { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    private WorkflowNotification() { }

    public WorkflowNotification(
        int recipientUserId,
        long? escalationId,
        string message,
        DateTime createdAtUtc)
        : this(
            recipientUserId,
            null,
            escalationId,
            WorkflowNotificationType.Escalation,
            null,
            message,
            createdAtUtc)
    {
    }

    public WorkflowNotification(
        int recipientUserId,
        int? requestId,
        long? escalationId,
        WorkflowNotificationType type,
        string? deduplicationKey,
        string message,
        DateTime createdAtUtc)
    {
        if (recipientUserId <= 0 ||
            requestId is <= 0 ||
            !Enum.IsDefined(type) ||
            deduplicationKey?.Length > 160 ||
            string.IsNullOrWhiteSpace(message) ||
            message.Length > 500)
        {
            throw new ArgumentException("Notification details are invalid.");
        }

        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Notification time must be UTC.",
                nameof(createdAtUtc));
        }

        RecipientUserId = recipientUserId;
        RequestId = requestId;
        EscalationId = escalationId;
        Type = type;
        DeduplicationKey = deduplicationKey;
        Message = message.Trim();
        CreatedAtUtc = createdAtUtc;
    }

    public void MarkRead(DateTime readAtUtc)
    {
        if (readAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Read time must be UTC.",
                nameof(readAtUtc));
        }

        ReadAtUtc ??= readAtUtc;
    }
}
