using FlowDesk.Application.Interfaces;
using Microsoft.Extensions.Hosting;

namespace FlowDisk.Api.Notifications;

public sealed class WorkflowNotificationWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<WorkflowNotificationWorker> logger)
    : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(
        ReadPositiveInt(configuration, "Notifications:WorkerIntervalSeconds", 60));
    private readonly int reminderMinutesBeforeDeadline = ReadPositiveInt(
        configuration,
        "Notifications:ReminderMinutesBeforeDeadline",
        60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Workflow notification background processing failed.");
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IApprovalWorkflowRepository>();
        var nowUtc = DateTime.UtcNow;
        await repository.ProcessOverdueEscalationsAsync(nowUtc);
        await repository.ProcessDeadlineRemindersAsync(
            nowUtc,
            reminderMinutesBeforeDeadline);

        if (!bool.TryParse(configuration["Email:Enabled"], out var emailEnabled) ||
            !emailEnabled)
        {
            return;
        }

        var sender = scope.ServiceProvider.GetRequiredService<IWorkflowEmailSender>();
        var batch = await repository.GetPendingEmailsAsync(nowUtc, 25);
        foreach (var email in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptAt = DateTime.UtcNow;
            if (!await repository.TryLeaseEmailAsync(
                    email.Id,
                    attemptAt,
                    attemptAt.AddMinutes(2)))
            {
                continue;
            }

            var sent = false;
            try
            {
                await sender.SendAsync(
                    email.RecipientEmail,
                    email.Subject,
                    email.Body,
                    cancellationToken);
                sent = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Workflow email outbox delivery failed for message {OutboxMessageId} ({FailureType}).",
                    email.Id,
                    exception.GetType().Name);
            }

            await repository.CompleteEmailAttemptAsync(
                email.Id,
                DateTime.UtcNow,
                sent);
        }
    }

    private static int ReadPositiveInt(
        IConfiguration configuration,
        string key,
        int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
        {
            return defaultValue;
        }

        if (!int.TryParse(configuration[key], out var value) || value <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' must be a positive integer.");
        }

        return value;
    }
}
