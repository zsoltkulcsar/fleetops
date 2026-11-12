using FleetOps.Api.Pipeline;
using FleetOps.Modules.Notifications;
namespace FleetOps.Api.Jobs;
public sealed class NotificationDigestJob(CommandDispatcher dispatcher) : IJob
{
    public string Name => "notifications-digest";
    public async Task<JobStep> RunStepAsync(JobContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.DispatchNotificationJobAsync(new(NotificationJobKind.Digest, context.Cursor), cancellationToken);
        return new(result.Done, result.Cursor, result.Processed);
    }
}
