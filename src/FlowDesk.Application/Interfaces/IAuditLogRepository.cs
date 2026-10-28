using FlowDesk.Application.DTOs;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public interface IAuditLogRepository
{
    void Add(AuditLogEntry entry);

    Task<AuditLogPageDto> GetPageAsync(
        int page,
        int pageSize,
        string? action,
        string? entityType,
        DateTime? fromUtc,
        DateTime? toUtc);
}
