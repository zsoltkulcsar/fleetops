namespace FleetOps.Api;
public sealed record AbsenceEditRequest(DateOnly From, DateOnly To, FleetOps.Modules.Absences.HalfDay? HalfDay, [property: System.Text.Json.Serialization.JsonRequired] FleetOps.Modules.Absences.AbsenceType Type, string? Reason, bool ConfirmOverlap = false);
