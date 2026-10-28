namespace FlowDesk.Domain.Entities;

public sealed class AuditLogEntry
{
    public long Id { get; private set; }

    public int ActorUserId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public long EntityId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public string? Details { get; private set; }

    private AuditLogEntry()
    {
    }

    public AuditLogEntry(
        int actorUserId,
        string action,
        string entityType,
        long entityId,
        DateTime occurredAtUtc,
        string? details = null)
    {
        if (actorUserId <= 0 || entityId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actorUserId));
        }

        if (string.IsNullOrWhiteSpace(action) || action.Length > 60)
        {
            throw new ArgumentException(
                "A valid audit action is required.",
                nameof(action));
        }

        if (string.IsNullOrWhiteSpace(entityType) || entityType.Length > 60)
        {
            throw new ArgumentException(
                "A valid audit entity type is required.",
                nameof(entityType));
        }

        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Audit event time must be UTC.",
                nameof(occurredAtUtc));
        }

        if (details?.Length > 500)
        {
            throw new ArgumentException(
                "Audit details cannot exceed 500 characters.",
                nameof(details));
        }

        ActorUserId = actorUserId;
        Action = action.Trim();
        EntityType = entityType.Trim();
        EntityId = entityId;
        OccurredAtUtc = occurredAtUtc;
        Details = details;
    }
}
