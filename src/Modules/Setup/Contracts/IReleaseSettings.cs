using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
public interface IReleaseSettings
{
    Task<IReadOnlyList<string>> GetReleasableDocTypesAsync(CommandContext? context, CancellationToken cancellationToken);
}
