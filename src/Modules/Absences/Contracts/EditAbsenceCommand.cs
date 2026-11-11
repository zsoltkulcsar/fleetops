using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
[CommandPolicy("absences.manage")]
public sealed record EditAbsenceCommand(Guid Id, DateOnly From, DateOnly To, HalfDay? HalfDay, AbsenceType Type, string? Reason, bool ConfirmOverlap = false) : ICommand<AbsenceView>;
