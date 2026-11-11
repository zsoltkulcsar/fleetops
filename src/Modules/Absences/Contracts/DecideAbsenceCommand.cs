using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
[CommandPolicy("absences.manage")]
public sealed record DecideAbsenceCommand(Guid Id, bool Approve, string? Reason = null, bool ConfirmOverlap = false) : ICommand<AbsenceView>;
