namespace FlowDesk.Domain.Entities;

public sealed class WorkflowEmailOutboxMessage
{
    public long Id { get; private set; }
    public int RecipientUserId { get; private set; }
    public User RecipientUser { get; private set; } = null!;
    public long NotificationId { get; private set; }
    public WorkflowNotification Notification { get; private set; } = null!;
    public string RecipientEmail { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string DeduplicationKey { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? LastAttemptAtUtc { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public int AttemptCount { get; private set; }

    private WorkflowEmailOutboxMessage() { }

    public WorkflowEmailOutboxMessage(
        int recipientUserId,
        long notificationId,
        string recipientEmail,
        string subject,
        string body,
        string deduplicationKey,
        DateTime createdAtUtc)
    {
        if (recipientUserId <= 0 ||
            notificationId <= 0 ||
            string.IsNullOrWhiteSpace(recipientEmail) ||
            recipientEmail.Length > 150 ||
            string.IsNullOrWhiteSpace(subject) ||
            subject.Length > 200 ||
            string.IsNullOrWhiteSpace(body) ||
            body.Length > 4000 ||
            string.IsNullOrWhiteSpace(deduplicationKey) ||
            deduplicationKey.Length > 160)
        {
            throw new ArgumentException("Email outbox details are invalid.");
        }

        EnsureUtc(createdAtUtc);
        RecipientUserId = recipientUserId;
        NotificationId = notificationId;
        RecipientEmail = recipientEmail.Trim();
        Subject = subject.Trim();
        Body = body.Trim();
        DeduplicationKey = deduplicationKey;
        CreatedAtUtc = createdAtUtc;
    }

    public void RecordAttempt(DateTime attemptedAtUtc)
    {
        EnsureUtc(attemptedAtUtc);
        LastAttemptAtUtc = attemptedAtUtc;
        AttemptCount++;
    }

    public void Lease(DateTime leaseUntilUtc)
    {
        EnsureUtc(leaseUntilUtc);
        LeaseUntilUtc = leaseUntilUtc;
    }

    public void MarkSent(DateTime sentAtUtc)
    {
        EnsureUtc(sentAtUtc);
        SentAtUtc ??= sentAtUtc;
        LeaseUntilUtc = null;
    }

    public void ReleaseLease()
    {
        LeaseUntilUtc = null;
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be UTC.");
        }
    }
}
