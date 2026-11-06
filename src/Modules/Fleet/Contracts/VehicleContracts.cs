using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record RequiredDocument(string Type, bool NeedsConfirmation);

[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record VehicleDetail(Guid Id, VehicleKind Kind, string Plate, string Vin, string Make, string Model, int Year,
    int GrossWeightKg, VehicleOwnership Ownership, DateOnly? LeaseEndDate, TrailerType? TrailerType, int? OdometerKm,
    Guid DepotId, VehicleStatus Status, int Version, IReadOnlyList<string> PlateHistory, IReadOnlyList<RequiredDocument> RequiredDocuments) : IReadModel;

[CommandPolicy("vehicles.manage")]
public sealed record CreateVehicleCommand(VehicleKind Kind, string Plate, string Vin, string Make, string Model, int Year,
    int GrossWeightKg, string? RequiredLicenceClass, VehicleOwnership Ownership, DateOnly? LeaseEndDate, TrailerType? TrailerType,
    int? OdometerKm, Guid? DepotId, int? ServiceIntervalKm, int? ServiceIntervalMonths, DateOnly? LastTachoDownloadAt, string Reason) : ICommand<VehicleDetail>;

 [CommandPolicy("vehicles.manage", HasStatePrecondition = true)]
public sealed record EditVehicleCommand(Guid? SubjectId, int Version, VehicleKind Kind, string Plate, string Vin, string Make, string Model,
    int Year, int GrossWeightKg, string? RequiredLicenceClass, VehicleOwnership Ownership, DateOnly? LeaseEndDate, TrailerType? TrailerType,
    int? OdometerKm, Guid? DepotId, int? ServiceIntervalKm, int? ServiceIntervalMonths, DateOnly? LastTachoDownloadAt, string Reason) : ICommand<VehicleDetail>, IHasSubject, IHasReason
{
}

public interface IHasReason
{
    string Reason { get; }
}

public static class VehicleRules
{
    public static readonly System.Text.RegularExpressions.Regex PlatePattern = new("^[A-ZÄÖÜ]{1,3}-[A-ZÄÖÜ]{1,2} [0-9]{1,4}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    public static readonly System.Text.RegularExpressions.Regex VinPattern = new("^[A-HJ-NPR-Z0-9]{17}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static IReadOnlyList<string> Validate(VehicleKind kind, string plate, string vin, string make, string model, int year,
        int grossWeightKg, VehicleOwnership ownership, DateOnly? leaseEndDate, TrailerType? trailerType, int? odometerKm, string reason)
    {
        var fields = new List<string>();
        if (!PlatePattern.IsMatch(plate)) fields.Add("plate");
        if (!VinPattern.IsMatch(vin)) fields.Add("vin");
        if (string.IsNullOrWhiteSpace(make)) fields.Add("make");
        if (string.IsNullOrWhiteSpace(model)) fields.Add("model");
        if (year is < 1900 or > 2200) fields.Add("year");
        if (grossWeightKg <= 0) fields.Add("grossWeightKg");
        if (ownership == VehicleOwnership.Owned && leaseEndDate.HasValue) fields.Add("leaseEndDate");
        if (ownership != VehicleOwnership.Owned && !leaseEndDate.HasValue) fields.Add("leaseEndDate");
        if (kind == VehicleKind.Truck && trailerType.HasValue) fields.Add("trailerType");
        if (kind == VehicleKind.Trailer && odometerKm.HasValue) fields.Add("odometerKm");
        if (kind == VehicleKind.Trailer && string.IsNullOrWhiteSpace(reason)) fields.Add("reason");
        return fields;
    }

    public static IReadOnlyList<RequiredDocument> Documents(VehicleKind kind, int grossWeightKg)
    {
        if (kind == VehicleKind.Trailer) return new List<RequiredDocument> { new("registration", false), new("HU", false), new("SP", true), new("insurance", false) };
        var documents = new List<RequiredDocument> { new("registration", false), new("HU", false) };
        if (grossWeightKg > 7500) documents.Add(new("SP", false));
        documents.Add(new("tacho_calibration", false)); documents.Add(new("insurance", false)); documents.Add(new("community_licence", false));
        return documents;
    }
}
