using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
[CommandPolicy("alerts.release.acknowledge")]
public sealed record AcknowledgeReleaseCommand(Guid AlertId, int Version) : ICommand<ReleaseReviewResult>;
