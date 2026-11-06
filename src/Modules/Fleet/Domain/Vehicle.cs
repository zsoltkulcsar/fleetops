using FleetOps.Modules.Audit;
using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

public enum VehicleKind { Truck, Trailer }
public enum VehicleOwnership { Owned, Leased, RentedIn }
public enum VehicleStatus { Active, Retired }
public enum TrailerType { Curtainsider, Box, Reefer, Flatbed, Tanker }

[AuditedEntity]
public sealed class Vehicle
{
    [AuditField(AuditFieldClasses.Technical)] public Guid Id { get; set; } = Guid.CreateVersion7();
    [AuditField(AuditFieldClasses.Plain)] public VehicleKind Kind { get; set; }
    [AuditField(AuditFieldClasses.Special)] public string Plate { get; set; } = "";
    [AuditField(AuditFieldClasses.Special)] public string Vin { get; set; } = "";
    [AuditField(AuditFieldClasses.Plain)] public string Make { get; set; } = "";
    [AuditField(AuditFieldClasses.Plain)] public string Model { get; set; } = "";
    [AuditField(AuditFieldClasses.Plain)] public int Year { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public int GrossWeightKg { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public string? RequiredLicenceClass { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public VehicleOwnership Ownership { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly? LeaseEndDate { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public TrailerType? TrailerType { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public int? OdometerKm { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateTimeOffset? OdometerUpdatedAt { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid DepotId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public int? ServiceIntervalKm { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public int? ServiceIntervalMonths { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly? LastTachoDownloadAt { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public VehicleStatus Status { get; set; } = VehicleStatus.Active;
    [AuditField(AuditFieldClasses.Technical)] public int Version { get; set; } = 1;
    [AuditField(AuditFieldClasses.Technical)] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [AuditField(AuditFieldClasses.Technical)] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed class PlateHistory
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid VehicleId { get; set; }
    public string Plate { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
