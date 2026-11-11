using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;

[CommandPolicy("tacho.manage")]
public sealed record LogTruckTachoDownloadCommand(Guid Id, DateOnly Date) : ICommand<TruckTachoDownloadResult>;
