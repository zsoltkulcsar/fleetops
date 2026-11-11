namespace FleetOps.Modules.Setup;
public sealed class ComplianceRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string GuideValue { get; set; } = "";
    public string? Value { get; set; }
    public string ValueSchema { get; set; } = "{}";
    public bool Confirmed { get; set; }
}
