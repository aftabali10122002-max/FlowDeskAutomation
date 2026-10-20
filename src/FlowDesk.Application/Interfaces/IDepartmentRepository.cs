using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public interface IDepartmentRepository
{
    Task<List<Department>> GetAllAsync();

    Task<Department?> GetByIdAsync(int id);

    Task AddAsync(Department department);

    Task SaveChangesAsync();
}
