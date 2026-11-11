using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public sealed record CorrectionRequestFact(bool Open, bool RequiresHr);
public interface ICorrectionRequestFacts
{
    Task<CorrectionRequestFact?> ReadAsync(Guid requestId, CommandContext context, CancellationToken cancellationToken);
}
