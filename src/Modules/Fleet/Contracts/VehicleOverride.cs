namespace FleetOps.Modules.Fleet;
public sealed class VehicleOverride
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public Guid By { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public DateTimeOffset ValidUntil { get; set; }
    public string[] DocTypes { get; set; } = [];
}
