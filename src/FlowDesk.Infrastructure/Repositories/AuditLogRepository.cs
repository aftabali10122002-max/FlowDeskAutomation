using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Repositories;

public sealed class AuditLogRepository(AppDbContext context)
    : IAuditLogRepository
{
    public void Add(AuditLogEntry entry)
    {
        context.AuditLogEntries.Add(entry);
    }

    public async Task<AuditLogPageDto> GetPageAsync(
        int page,
        int pageSize,
        string? action,
        string? entityType,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        var query =
            from entry in context.AuditLogEntries.AsNoTracking()
            join actor in context.Users.AsNoTracking()
                on entry.ActorUserId equals actor.Id into actors
            from actor in actors.DefaultIfEmpty()
            select new { Entry = entry, ActorName = actor == null ? null : actor.FullName };

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(x => x.Entry.Action.Contains(action));
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(x => x.Entry.EntityType.Contains(entityType));
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.Entry.OccurredAtUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.Entry.OccurredAtUtc <= toUtc.Value);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(x => x.Entry.OccurredAtUtc)
            .ThenByDescending(x => x.Entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AuditLogItemDto
            {
                Id = x.Entry.Id,
                ActorUserId = x.Entry.ActorUserId,
                ActorName = x.ActorName,
                Action = x.Entry.Action,
                EntityType = x.Entry.EntityType,
                EntityId = x.Entry.EntityId,
                OccurredAtUtc = DateTime.SpecifyKind(
                    x.Entry.OccurredAtUtc,
                    DateTimeKind.Utc),
                Details = x.Entry.Details
            })
            .ToListAsync();

        return new AuditLogPageDto
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
