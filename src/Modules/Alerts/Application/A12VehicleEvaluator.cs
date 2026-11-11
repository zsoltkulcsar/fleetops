namespace FleetOps.Modules.Alerts;

public sealed class A12VehicleEvaluator(ITachoDownloadFacts facts) : TachoDownloadEvaluator(facts)
{
    public override string SubjectType => "vehicle";
    protected override string Setting => "tacho.vehicle_unit_days";
}
