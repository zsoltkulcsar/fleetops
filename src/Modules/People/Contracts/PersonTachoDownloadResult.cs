using FleetOps.Kernel.Authorization;
namespace FleetOps.People;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record PersonTachoDownloadResult(Guid Id, DateOnly LastTachoDownloadAt) : IReadModel;
