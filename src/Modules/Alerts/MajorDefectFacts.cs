using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;

public interface IMajorDefectFacts
{
    Task<DateTimeOffset?> OpenUnlinkedMajorReportedAtAsync(Guid defectId, CommandContext context, CancellationToken cancellationToken);
}
