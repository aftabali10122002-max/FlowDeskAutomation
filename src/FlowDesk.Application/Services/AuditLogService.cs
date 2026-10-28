using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Services;

public sealed class AuditLogService(
    IAuditLogRepository auditLogRepository,
    IUserRepository userRepository)
{
    public async Task<AuditLogPageDto> GetPageAsync(
        int adminUserId,
        int page,
        int pageSize,
        string? action,
        string? entityType,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        var admin = adminUserId > 0
            ? await userRepository.GetByIdAsync(adminUserId)
            : null;
        if (admin is not { IsActive: true } ||
            !string.Equals(admin.Role, UserRoles.Admin, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "Only an active Administrator can view audit history.");
        }

        if (page < 1 || pageSize is < 1 or > 100 ||
            ((long)page - 1) * pageSize > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        if (fromUtc is { Kind: not DateTimeKind.Utc } ||
            toUtc is { Kind: not DateTimeKind.Utc } ||
            (fromUtc.HasValue && toUtc.HasValue && fromUtc > toUtc))
        {
            throw new ArgumentException("The audit date range is invalid.");
        }

        return await auditLogRepository.GetPageAsync(
            page,
            pageSize,
            Normalize(action),
            Normalize(entityType),
            fromUtc,
            toUtc);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
