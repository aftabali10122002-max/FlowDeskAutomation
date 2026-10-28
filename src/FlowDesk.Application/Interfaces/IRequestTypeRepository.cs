using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public interface IRequestTypeRepository
{
    Task<List<RequestType>> GetEnabledAsync();

    Task<List<RequestType>> GetAllAsync();

    Task<RequestType?> GetEnabledByCodeAsync(string code);

    Task<RequestType?> GetTrackedByIdAsync(int id);

    Task SaveChangesAsync();
}
