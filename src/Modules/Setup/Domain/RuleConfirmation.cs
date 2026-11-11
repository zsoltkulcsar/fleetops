namespace FleetOps.Modules.Setup;
public sealed class RuleConfirmation
{
    public Guid Id { get; set; }
    public string RuleId { get; set; } = "";
    public string Value { get; set; } = "";
    public Guid ConfirmedBy { get; set; }
    public DateTimeOffset ConfirmedAt { get; set; }
}
