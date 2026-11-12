using FleetOps.Migrations;
namespace FleetOps.Api.Jobs;
public sealed class NotificationScheduleService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<NotificationScheduleService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Jobs:EnableNotificationScheduling", false)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<JobRunner>();
                await runner.RunAsync("alerts-escalation", JobTrigger.scheduler, stoppingToken);
                await runner.RunAsync("notifications-digest", JobTrigger.scheduler, stoppingToken);
                await runner.RunAsync("notifications-retry", JobTrigger.scheduler, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogWarning("Notification schedule failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            }
            if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
        }
    }
}
