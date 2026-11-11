using FleetOps.Kernel.Authorization;
using FleetOps.Kernel;

namespace FleetOps.Modules.Fleet;

[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record RequiredDocument(string Type, bool NeedsConfirmation);

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record VehicleDetail(
    Guid Id,
    VehicleKind Kind,
    string Plate,
    string Vin,
    string Make,
    string Model,
    int Year,
    int GrossWeightKg,
    VehicleOwnership Ownership,
    DateOnly? LeaseEndDate,
    TrailerType? TrailerType,
    int? OdometerKm,
    Guid DepotId,
    VehicleStatus Status,
    RetiredReason? RetiredReason,
    DateTimeOffset? RetiredAt,
    int Version,
    string? RequiredLicenceClass,
    int? ServiceIntervalKm,
    int? ServiceIntervalMonths,
    DateOnly? LastTachoDownloadAt,
    IReadOnlyList<string> PlateHistory,
    IReadOnlyList<RequiredDocument> RequiredDocuments,
    ReadinessShownStatus ShownStatus,
    bool Usable,
    IReadOnlyList<ReadinessCause> Causes) : IReadModel
{
    public DefectListResult? Defects { get; init; }
}

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record VehicleListItem(
    Guid Id,
    VehicleKind Kind,
    string Plate,
    string Make,
    string Model,
    int Year,
    VehicleStatus Status,
    VehicleOwnership Ownership,
    int Version,
    ReadinessShownStatus ShownStatus = ReadinessShownStatus.Active,
    bool Usable = false,
    IReadOnlyList<ReadinessCause>? Causes = null);

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record VehicleListResult(IReadOnlyList<VehicleListItem> Items, string? NextCursor) : IReadModel;

[QueryPolicy("vehicles.view")]
public sealed record GetVehicleQuery(Guid? SubjectId) : IQuery<VehicleDetail>, IHasSubject;

[QueryPolicy("vehicles.view")]
public sealed record ListVehiclesQuery(string? Cursor, int Limit, string? Search, string? Kind, bool IncludeRetired) : IQuery<VehicleListResult>;

public sealed record VehicleListCursor(string Plate, Guid Id);

public sealed record VehicleListPage(IReadOnlyList<VehicleListItem> Items, VehicleListCursor? NextCursor);

[CommandPolicy("vehicles.manage")]
public sealed record CreateVehicleCommand(
    VehicleKind Kind,
    string? Plate,
    string? Vin,
    string? Make,
    string? Model,
    int Year,
    int GrossWeightKg,
    string? RequiredLicenceClass,
    VehicleOwnership Ownership,
    DateOnly? LeaseEndDate,
    TrailerType? TrailerType,
    int? OdometerKm,
    Guid? DepotId,
    int? ServiceIntervalKm,
    int? ServiceIntervalMonths,
    DateOnly? LastTachoDownloadAt,
    string? Reason) : ICommand<VehicleDetail>;

[CommandPolicy("vehicles.manage", HasStatePrecondition = true)]
public sealed record EditVehicleCommand(
    Guid? SubjectId,
    int Version,
    VehicleKind Kind,
    string? Plate,
    string? Vin,
    string? Make,
    string? Model,
    int Year,
    int GrossWeightKg,
    string? RequiredLicenceClass,
    VehicleOwnership Ownership,
    DateOnly? LeaseEndDate,
    TrailerType? TrailerType,
    int? OdometerKm,
    Guid? DepotId,
    int? ServiceIntervalKm,
    int? ServiceIntervalMonths,
    DateOnly? LastTachoDownloadAt,
    string? Reason) : ICommand<VehicleDetail>, IHasSubject, IHasReason;

[CommandPolicy("vehicles.manage", HasStatePrecondition = true)]
public sealed record RetireVehicleCommand(Guid? SubjectId, int Version, RetiredReason RetiredReason) : ICommand<VehicleDetail>, IHasSubject
{
    public string? Reason => RetiredReason.ToString();
}

[CommandPolicy("vehicles.retire.undo")]
public sealed record UndoRetireVehicleCommand(Guid? SubjectId, int Version, string? Reason) : ICommand<VehicleDetail>, IHasSubject, IHasReason;

[CommandPolicy("vehicles.manage", HasStatePrecondition = true)]
public sealed record SetVehicleOffRoadCommand(Guid? SubjectId, int Version, bool ManualOffRoad, string? Reason) : ICommand<VehicleDetail>, IHasSubject, IHasReason;

public interface IHasReason
{
    string? Reason { get; }
}
public static class VehicleRules
{
 public static readonly System.Text.RegularExpressions.Regex PlatePattern = new("\\A[A-ZÄÖÜ]{1,3}-[A-ZÄÖÜ]{1,2} [0-9]{1,4}\\z", System.Text.RegularExpressions.RegexOptions.Compiled);
 public static readonly System.Text.RegularExpressions.Regex VinPattern = new("\\A[A-HJ-NPR-Z0-9]{17}\\z", System.Text.RegularExpressions.RegexOptions.Compiled);
 public static IReadOnlyList<string> Validate(VehicleKind kind,string? plate,string? vin,string? make,string? model,int year,int grossWeightKg,VehicleOwnership ownership,DateOnly? leaseEndDate,TrailerType? trailerType,int? odometerKm,string? reason,int? serviceIntervalKm=null,int? serviceIntervalMonths=null,DateOnly? lastTachoDownloadAt=null,string? requiredLicenceClass=null){var f=new List<string>();if(string.IsNullOrWhiteSpace(plate)||!PlatePattern.IsMatch(plate))f.Add("plate");if(string.IsNullOrWhiteSpace(vin)||!VinPattern.IsMatch(vin))f.Add("vin");if(string.IsNullOrWhiteSpace(make))f.Add("make");if(string.IsNullOrWhiteSpace(model))f.Add("model");if(!Enum.IsDefined(kind))f.Add("kind");if(!Enum.IsDefined(ownership))f.Add("ownership");if(trailerType.HasValue&&!Enum.IsDefined(trailerType.Value))f.Add("trailerType");if(kind==VehicleKind.Trailer&&!trailerType.HasValue)f.Add("trailerType");if(year is <1900 or >2200)f.Add("year");if(grossWeightKg<=0)f.Add("grossWeightKg");if(string.IsNullOrWhiteSpace(reason))f.Add("reason");if(odometerKm is <0)f.Add("odometerKm");if(serviceIntervalKm is <0)f.Add("serviceIntervalKm");if(serviceIntervalMonths is <0)f.Add("serviceIntervalMonths");if(ownership==VehicleOwnership.Owned&&leaseEndDate.HasValue)f.Add("leaseEndDate");if(ownership!=VehicleOwnership.Owned&&!leaseEndDate.HasValue)f.Add("leaseEndDate");if(kind==VehicleKind.Truck&&trailerType.HasValue)f.Add("trailerType");if(kind==VehicleKind.Trailer&&odometerKm.HasValue)f.Add("odometerKm");if(kind==VehicleKind.Trailer&&serviceIntervalKm.HasValue)f.Add("serviceIntervalKm");if(kind==VehicleKind.Trailer&&lastTachoDownloadAt.HasValue)f.Add("lastTachoDownloadAt");if(requiredLicenceClass is not null&&!new[]{"C","CE","C1","C1E","D","DE","D1","D1E"}.Contains(requiredLicenceClass,StringComparer.OrdinalIgnoreCase))f.Add("requiredLicenceClass");return f;}
 public static IReadOnlyList<RequiredDocument> Documents(VehicleKind kind,int grossWeightKg){if(kind==VehicleKind.Trailer)return[new("registration",false),new("HU",false),new("SP",true),new("insurance",false)];var d=new List<RequiredDocument>{new("registration",false),new("HU",false)};if(grossWeightKg>7500)d.Add(new("SP",false));d.Add(new("tacho_calibration",false));d.Add(new("insurance",false));d.Add(new("community_licence",false));return d;}
}
