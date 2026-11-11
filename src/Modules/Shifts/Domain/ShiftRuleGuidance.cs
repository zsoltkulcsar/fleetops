namespace FleetOps.Modules.Shifts;
public sealed class ShiftRuleGuidance
{
    public Guid ShiftId { get; set; }
    public string Warnings { get; set; } = "[]";
}
