using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
[CommandPolicy("absences.manage")]
public sealed record CancelAbsenceCommand(Guid Id, string? Reason) : ICommand<AbsenceView>;
