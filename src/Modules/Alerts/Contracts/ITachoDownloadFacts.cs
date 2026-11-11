using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;

public interface ITachoDownloadFacts
{
    Task<TachoDownloadFact> ReadAsync(string subjectType, Guid id, CommandContext context, CancellationToken cancellationToken);
}
