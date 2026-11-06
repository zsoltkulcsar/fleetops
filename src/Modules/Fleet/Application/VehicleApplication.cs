using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

public interface IVehicleStore
{
    Task<VehicleDetail> CreateAsync(CreateVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task<VehicleDetail> EditAsync(EditVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task<VehicleDetail> RetireAsync(RetireVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task<VehicleDetail> UndoRetireAsync(UndoRetireVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}

public sealed class CreateVehicleHandler(IVehicleStore store) : ICommandHandler<CreateVehicleCommand, VehicleDetail>
{
    public Task<VehicleDetail> HandleAsync(CreateVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        Validate(command.Kind, command.Plate, command.Vin, command.Make, command.Model, command.Year, command.GrossWeightKg, command.Ownership, command.LeaseEndDate, command.TrailerType, command.OdometerKm, command.Reason, command.ServiceIntervalKm, command.ServiceIntervalMonths, command.LastTachoDownloadAt, command.RequiredLicenceClass);
        return store.CreateAsync(command, actor, context, cancellationToken);
    }

    internal static void Validate(VehicleKind kind, string? plate, string? vin, string? make, string? model, int year, int grossWeightKg,
        VehicleOwnership ownership, DateOnly? leaseEndDate, TrailerType? trailerType, int? odometerKm, string? reason, int? serviceIntervalKm = null, int? serviceIntervalMonths = null, DateOnly? lastTachoDownloadAt = null, string? requiredLicenceClass = null)
    {
        var errors = VehicleRules.Validate(kind, plate, vin, make, model, year, grossWeightKg, ownership, leaseEndDate, trailerType, odometerKm, reason, serviceIntervalKm, serviceIntervalMonths, lastTachoDownloadAt, requiredLicenceClass);
        if (errors.Count > 0) throw new VehicleValidationException(errors);
    }
}

public sealed class EditVehicleHandler(IVehicleStore store) : ICommandHandler<EditVehicleCommand, VehicleDetail>
{
    public Task<VehicleDetail> HandleAsync(EditVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        CreateVehicleHandler.Validate(command.Kind, command.Plate, command.Vin, command.Make, command.Model, command.Year, command.GrossWeightKg,
            command.Ownership, command.LeaseEndDate, command.TrailerType, command.OdometerKm, command.Reason, command.ServiceIntervalKm, command.ServiceIntervalMonths, command.LastTachoDownloadAt, command.RequiredLicenceClass);
        if (command.SubjectId is not Guid) throw new VehicleValidationException(["id"]);
        return store.EditAsync(command, actor, context, cancellationToken);
    }
}

public sealed class RetireVehicleHandler(IVehicleStore store) : ICommandHandler<RetireVehicleCommand, VehicleDetail>
{
    public Task<VehicleDetail> HandleAsync(RetireVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (command.SubjectId is not Guid) throw new VehicleValidationException(["id"]);
        if (!Enum.IsDefined(command.RetiredReason)) throw new VehicleValidationException(["retiredReason"]);
        return store.RetireAsync(command, actor, context, cancellationToken);
    }
}

public sealed class UndoRetireVehicleHandler(IVehicleStore store) : ICommandHandler<UndoRetireVehicleCommand, VehicleDetail>
{
    public Task<VehicleDetail> HandleAsync(UndoRetireVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (command.SubjectId is not Guid) throw new VehicleValidationException(["id"]);
        if (string.IsNullOrWhiteSpace(command.Reason)) throw new VehicleValidationException(["reason"]);
        return store.UndoRetireAsync(command, actor, context, cancellationToken);
    }
}

public sealed class VehicleValidationException(IReadOnlyList<string> fields) : Exception("vehicle_validation")
{
    public IReadOnlyList<string> Fields { get; } = fields;
}

public sealed class VehicleConflictException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class InvalidCursorException : Exception
{
    public InvalidCursorException() : base("invalid_cursor") { }
}
