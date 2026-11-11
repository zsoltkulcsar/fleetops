using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
[CommandPolicy("absences.manage")]
public sealed record AcknowledgeAbsenceCommand(Guid AlertId, int Version) : ICommand<ReleaseReviewResult>;
