using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Alerts;

public interface ICriticalDefectFacts
{
    Task<bool> IsOpenCriticalAsync(Guid defectId, CommandContext context, CancellationToken cancellationToken);
}
