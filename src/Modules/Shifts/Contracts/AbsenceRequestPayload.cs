using FleetOps.Modules.Absences;
namespace FleetOps.Modules.Shifts;
public sealed record AbsenceRequestPayload(DateOnly From, DateOnly To, HalfDay? HalfDay = null, string? Note = null);
