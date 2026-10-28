using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlowDesk.Infrastructure.Persistence;

public sealed class OfflineAppDbContextFactory
    : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(
                "Server=FLOWDESK_OFFLINE_DESIGN_ONLY;Database=FlowDeskOfflineDesignOnly;")
            .Options;

        return new AppDbContext(options);
    }
}
