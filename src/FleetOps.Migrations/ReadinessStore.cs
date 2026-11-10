using System.Text.Json;
using FleetOps.Kernel;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Readiness;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfReadinessStore(FleetOpsDbContext db) : IReadiness
{
    public async Task<DriverReadiness?> ForDriverAtAsync(Guid employeeId, DateOnly date, DateTimeOffset instant, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(employee => employee.Id == employeeId, cancellationToken);
        if (employee is null) return null;
        var documents = await db.Documents.AsNoTracking().Where(document => document.OwnerType == DocumentOwnerType.Employee && document.OwnerId == employeeId &&
            (document.Status == DocumentStatus.Verified || document.Status == DocumentStatus.Replaced) && (document.VerifiedAt ?? document.CreatedAt) <= instant).ToListAsync(cancellationToken);
        var superseded = await db.Documents.AsNoTracking().Where(document => document.OwnerType == DocumentOwnerType.Employee && document.OwnerId == employeeId &&
            document.ReplacesId != null && document.VerifiedAt <= instant).Select(document => document.ReplacesId!.Value).ToListAsync(cancellationToken);
        var validAtStart = documents.Where(document => !superseded.Contains(document.Id)).ToArray();
        foreach (var document in validAtStart) document.Status = DocumentStatus.Verified;
        return Driver(employee, validAtStart, date);
    }
    public async Task<IReadOnlyDictionary<Guid, VehicleReadiness>> ForVehiclesAsync(IReadOnlyCollection<Guid> ids, DateOnly date, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return new Dictionary<Guid, VehicleReadiness>();
        var vehicles = await db.Vehicles.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        var documents = await db.Documents.AsNoTracking().Where(x => x.OwnerType == DocumentOwnerType.Vehicle && ids.Contains(x.OwnerId) && x.Status != DocumentStatus.Replaced).ToListAsync(cancellationToken);
        return vehicles.ToDictionary(x => x.Id, x => Vehicle(x, documents.Where(d => d.OwnerId == x.Id), date));
    }

    public async Task<IReadOnlyDictionary<Guid, DriverReadiness>> ForDriversAsync(IReadOnlyCollection<Guid> ids, DateOnly date, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return new Dictionary<Guid, DriverReadiness>();
        var employees = await db.Employees.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        var documents = await db.Documents.AsNoTracking().Where(x => x.OwnerType == DocumentOwnerType.Employee && ids.Contains(x.OwnerId) && x.Status != DocumentStatus.Replaced).ToListAsync(cancellationToken);
        return employees.ToDictionary(x => x.Id, x => Driver(x, documents.Where(d => d.OwnerId == x.Id), date));
    }

    private static VehicleReadiness Vehicle(Vehicle vehicle, IEnumerable<Document> documents, DateOnly date)
    {
        var causes = RequiredVehicleTypes(vehicle).Select(type => CauseFor(type, documents, date)).Where(x => x is not null).Cast<ReadinessCause>().ToList();
        if (vehicle.ManualOffRoad) causes.Insert(0, new(ReadinessCauseCode.ManualOffRoad, null));
        if (vehicle.Status == VehicleStatus.Retired) causes.Add(new(ReadinessCauseCode.Retired, null));
        var shown = vehicle.Status == VehicleStatus.Retired ? ReadinessShownStatus.Retired : causes.Any(x => x.Code == ReadinessCauseCode.ManualOffRoad || x.Code == ReadinessCauseCode.DocumentExpired) ? ReadinessShownStatus.OffRoad : ReadinessShownStatus.Active;
        return new(vehicle.Status == VehicleStatus.Active && causes.Count == 0, shown, causes);
    }

    private static DriverReadiness Driver(Employee employee, IEnumerable<Document> documents, DateOnly date)
    {
        var causes = new List<ReadinessCause>();
        foreach (var type in new[] { DocumentType.LicenceC_CE, DocumentType.DriverCard, DocumentType.Code95, DocumentType.Medical })
        {
            var cause = CauseFor(type, documents, date);
            if (cause is not null) causes.Add(cause);
        }
        var roles = JsonSerializer.Deserialize<string[]>(employee.Roles) ?? [];
        if (!roles.Contains("driver", StringComparer.OrdinalIgnoreCase)) causes.Add(new(ReadinessCauseCode.NotADriver, null));
        if (employee.Status == EmployeeStatus.Suspended) causes.Add(new(ReadinessCauseCode.Suspended, null));
        if (employee.Status == EmployeeStatus.Left) causes.Add(new(ReadinessCauseCode.Left, null));
        return new(employee.Status == EmployeeStatus.Active && roles.Contains("driver", StringComparer.OrdinalIgnoreCase) && causes.Count == 0, causes);
    }

    private static ReadinessCause? CauseFor(DocumentType type, IEnumerable<Document> documents, DateOnly date)
    {
        var verified = documents.Where(x => x.Type == type && x.Status == DocumentStatus.Verified).OrderByDescending(x => x.Version).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefault();
        if (verified is null) return new(ReadinessCauseCode.DocumentMissing, TypeName(type));
        return verified.ExpiresAt is not null && verified.ExpiresAt.Value < date ? new(ReadinessCauseCode.DocumentExpired, TypeName(type)) : null;
    }

    private static IEnumerable<DocumentType> RequiredVehicleTypes(Vehicle vehicle) => VehicleRules.Documents(vehicle.Kind, vehicle.GrossWeightKg).Where(x => DocumentsCatalog.Get(Map(x.Type)).Blocking).Select(x => Map(x.Type));
    private static DocumentType Map(string type) => type switch { "registration" => DocumentType.Registration, "HU" => DocumentType.HuReport, "SP" => DocumentType.SpReport, "tacho_calibration" => DocumentType.TachoCalibration, "insurance" => DocumentType.Insurance, "community_licence" => DocumentType.CommunityLicence, _ => throw new InvalidOperationException("unknown_document_type") };
    private static string TypeName(DocumentType type) => type switch { DocumentType.HuReport => "hu_report", DocumentType.SpReport => "sp_report", DocumentType.TachoCalibration => "tacho_calibration", DocumentType.CommunityLicence => "community_licence", DocumentType.LicenceC_CE => "licence_c_ce", DocumentType.DriverCard => "driver_card", DocumentType.Code95 => "code95", DocumentType.Medical => "medical", DocumentType.Registration => "registration", DocumentType.Insurance => "insurance", _ => type.ToString().ToSnakeCase() };
}
