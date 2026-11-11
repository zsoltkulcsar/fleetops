namespace FleetOps.Api;
public sealed record AbsenceDecisionRequest([property: System.Text.Json.Serialization.JsonRequired] bool Approve, string? Reason = null, bool ConfirmOverlap = false);
