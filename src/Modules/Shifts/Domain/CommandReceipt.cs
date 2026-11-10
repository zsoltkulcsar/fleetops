namespace FleetOps.Modules.Shifts;

public sealed class CommandReceipt
{
    public Guid EmployeeId { get; set; }
    public Guid CommandId { get; set; }
    public Guid AggregateId { get; set; }
    public string Type { get; set; } = "";
    public int SchemaVersion { get; set; }
    public int BaseVersion { get; set; }
    public DateTimeOffset DeviceTime { get; set; }
    public DateTimeOffset ServerReceivedAt { get; set; }
    public string Payload { get; set; } = "{}";
    public string Result { get; set; } = "{}";
    public string Outcome { get; set; } = "";
}
