namespace FleetOps.Modules.Shifts;

public sealed class ConflictRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public Guid ShiftId { get; set; }
    public Guid CommandId { get; set; }
    public int BaseVersion { get; set; }
    public int OfficeVersion { get; set; }
    public string DriverPayload { get; set; } = "{}";
    public string OfficeSnapshot { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}
