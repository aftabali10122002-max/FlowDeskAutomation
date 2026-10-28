using FlowDesk.Application.Interfaces;

namespace FlowDisk.Api.Leave;

public sealed class LeaveBalanceMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LeaveBalanceMaintenanceWorker> logger,
    IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var businessTimeZoneId = configuration[
            "Leave:BusinessTimeZoneId"] ?? throw new InvalidOperationException(
            "Leave:BusinessTimeZoneId must be configured.");
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider
                .GetRequiredService<ILeaveAccountingRepository>();
            var converted = await repository
                .ProcessDueScheduledCommitmentsAsync(DateTime.UtcNow);
            var nowUtc = DateTime.UtcNow;
            var carryForwardAndExpiries = await repository
                .ProcessDueCarryForwardAndExpiryAsync(
                    nowUtc,
                    businessTimeZoneId);
            var accruals = await repository.ProcessDueAccrualsAsync(
                nowUtc,
                businessTimeZoneId);
            if (converted > 0)
            {
                logger.LogInformation(
                    "Converted {Count} due scheduled leave commitments to used leave.",
                    converted);
            }

            if (carryForwardAndExpiries > 0)
            {
                logger.LogInformation(
                    "Posted {Count} leave carry-forward or expiry ledger entries.",
                    carryForwardAndExpiries);
            }

            if (accruals > 0)
            {
                logger.LogInformation(
                    "Posted {Count} leave accrual ledger entries.",
                    accruals);
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
