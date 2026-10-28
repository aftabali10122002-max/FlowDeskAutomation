using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Repositories;

public sealed class RequestTypeRepository(AppDbContext context)
    : IRequestTypeRepository
{
    public Task<List<RequestType>> GetEnabledAsync()
    {
        return context.RequestTypes
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.DisplayName)
            .ToListAsync();
    }

    public Task<List<RequestType>> GetAllAsync()
    {
        return context.RequestTypes
            .AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .ToListAsync();
    }

    public Task<RequestType?> GetEnabledByCodeAsync(string code)
    {
        return context.RequestTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == code && x.IsEnabled);
    }

    public Task<RequestType?> GetTrackedByIdAsync(int id)
    {
        return context.RequestTypes
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public Task SaveChangesAsync() => context.SaveChangesAsync();
}
