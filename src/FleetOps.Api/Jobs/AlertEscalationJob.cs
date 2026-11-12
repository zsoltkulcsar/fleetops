using FleetOps.Api.Pipeline;
using FleetOps.Modules.Notifications;
namespace FleetOps.Api.Jobs;
public sealed class AlertEscalationJob(CommandDispatcher dispatcher) : IJob
{
    public string Name => "alerts-escalation";
    public async Task<JobStep> RunStepAsync(JobContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.DispatchNotificationJobAsync(new(NotificationJobKind.Escalation, context.Cursor), cancellationToken);
        return new(result.Done, result.Cursor, result.Processed);
    }
}
