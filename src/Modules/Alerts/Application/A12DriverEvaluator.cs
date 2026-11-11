namespace FleetOps.Modules.Alerts;

public sealed class A12DriverEvaluator(ITachoDownloadFacts facts) : TachoDownloadEvaluator(facts)
{
    public override string SubjectType => "employee";
    protected override string Setting => "tacho.driver_card_days";
}
