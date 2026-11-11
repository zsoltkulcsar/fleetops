using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Schedule;
[CommandPolicy("schedule.edit")]
public sealed record DecideScheduleRequestCommand(Guid Id, bool Accept, string? Reason = null) : ICommand<ScheduleRequestView>;
