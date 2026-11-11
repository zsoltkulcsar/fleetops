using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
[CommandPolicy("absences.record")]
public sealed record RecordAbsenceCommand(Guid EmployeeId, DateOnly From, DateOnly To, HalfDay? HalfDay, [property: System.Text.Json.Serialization.JsonRequired] AbsenceType Type, string? Note = null, bool ConfirmOverlap = false) : ICommand<AbsenceView>;
