namespace FleetOps.Modules.Shifts;
public sealed class CorrectionRequest
{
    public Guid Id { get; set; }
    public Guid CommandId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? ShiftId { get; set; }
    public DateOnly Date { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset? ProposedStartAt { get; set; }
    public DateTimeOffset? ProposedEndAt { get; set; }
    public string? Note { get; set; }
    public bool RequiresHr { get; set; }
    public string Status { get; set; } = "open";
    public string Before { get; set; } = "{}";
    public string Diffs { get; set; } = "[]";
    public string? DeclineReason { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset DeviceTime { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
