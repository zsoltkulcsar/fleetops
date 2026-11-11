using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

public interface IOfficeDefectStore
{
    Task SaveAsync(DefectReport report, Guid actorId, string reason, CommandContext context, CancellationToken cancellationToken);
    Task ValidateReporterAsync(Guid reporterId, Guid actorId, CommandContext context, CancellationToken cancellationToken);
    Task ValidatePhotosAsync(DefectReport report, Guid actorId, CommandContext context, CancellationToken cancellationToken);
}
