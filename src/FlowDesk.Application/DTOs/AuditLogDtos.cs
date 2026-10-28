namespace FlowDesk.Application.DTOs;

public sealed class AuditLogItemDto
{
    public long Id { get; init; }

    public int ActorUserId { get; init; }

    public string? ActorName { get; init; }

    public string Action { get; init; } = string.Empty;

    public string EntityType { get; init; } = string.Empty;

    public long EntityId { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    public string? Details { get; init; }
}

public sealed class AuditLogPageDto
{
    public IReadOnlyList<AuditLogItemDto> Items { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalCount { get; init; }
}
