using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
namespace FleetOps.Modules.Notifications;
public sealed class OwnerOverviewHandler(IOwnerOverviewStore store) : IQueryHandler<GetOwnerOverviewQuery, OwnerOverview>
{
    public async Task<OwnerOverview> HandleAsync(GetOwnerOverviewQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var facts = await store.ReadAsync(cancellationToken);
        var required = facts.Drivers.SelectMany(id => new[] { "LicenceC_CE", "DriverCard", "Code95", "Medical" }.Select(type => new OwnerDocumentFact(id, true, type))).ToList();
        foreach (var vehicle in facts.Vehicles)
        foreach (var requirement in VehicleRules.Documents(vehicle.Kind, vehicle.GrossWeightKg))
        {
            var type = requirement.Type switch
            {
                "registration" => "Registration",
                "HU" => "HuReport",
                "SP" => "SpReport",
                "tacho_calibration" => "TachoCalibration",
                "insurance" => "Insurance",
                "community_licence" => "CommunityLicence",
                _ => throw new InvalidOperationException("unknown_document_type")
            };
            required.Add(new(vehicle.Id, false, type));
        }
        var complete = required.Count(facts.CurrentDocuments.Contains);
        return new([
            new("criticalAlerts", facts.CriticalAlerts, null, "/?severity=critical"),
            new("trucksOffRoad", facts.Vehicles.Count(vehicle => vehicle.Kind == VehicleKind.Truck && vehicle.OffRoad), null, "/fleet?status=off_road"),
            new("documentsCompletePercent", required.Count == 0 ? null : decimal.Round(100m * complete / required.Count, 2), null, "/documents"),
            new("exportStatus", null, facts.ExportStatus ?? "not_generated", "/payroll"),
            new("maintenanceCostEur", facts.MaintenanceCostEur, null, "/fleet/maintenance")]);
    }
}

