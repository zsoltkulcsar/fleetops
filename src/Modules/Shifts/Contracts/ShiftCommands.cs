using System.Text.Json;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Shifts;

public sealed record DriverCommand(Guid CommandId, string Type, int SchemaVersion, Guid AggregateId, int BaseVersion, DateTimeOffset DeviceTime, JsonElement Payload);

[VisibleTo(RoleCode.Driver)]
public sealed record DriverCommandResult(Guid CommandId, string Status, string? Code, ShiftView? Shift) : IReadModel;

[CommandPolicy("shifts.enter_edit", HasStatePrecondition = true)]
public sealed record ApplyDriverCommand(Guid? SubjectId, DriverCommand Command) : ICommand<DriverCommandResult>, IHasSubject;

[VisibleTo(RoleCode.Driver)]
public sealed record ShiftView(Guid Id, DateOnly Date, DateTimeOffset StartAt, DateTimeOffset? EndAt, Guid? TruckId, Guid? TrailerId,
    ShiftStatus Status, int Version, string? RejectReason, string? BlockedAtStart, bool DeviceTimeGap,
    IReadOnlyList<ActivityView> Activities, IReadOnlyList<OvernightView> Overnights)
{
    public IReadOnlyList<ShiftHistory> OfficeChanges { get; init; } = [];
    public DateTimeOffset? SubmittedAt { get; init; }
    public ShiftSource Source { get; init; }
}
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ActivityView(Guid Id, ActivityType Type, DateTimeOffset StartAt, DateTimeOffset? EndAt);
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record OvernightView(Guid Id, DateOnly Night, string CountryCode, OvernightKind Kind);

[VisibleTo(RoleCode.Driver)]
public sealed record ShiftSnapshot(DateTimeOffset GeneratedAt, string CompanyTimeZone, IReadOnlyList<ShiftView> Shifts, IReadOnlyList<Guid> AppliedCommandIds, IReadOnlyList<ShiftTruckChoice> Trucks, Guid? SuggestedTruckId, IReadOnlyList<DriverShiftConflict> Conflicts, IReadOnlyList<DriverTruckAssignment> Assignments) : IReadModel;
public sealed record ShiftTruckChoice(Guid Id, string Plate, string Kind, bool Usable, string? Reason, bool LicenceClassWarning);
public sealed record DriverShiftConflict(Guid CommandId, Guid ShiftId, string Action, string ChangedBy, IReadOnlyList<string> ChangedFields, DateTimeOffset? ProposedStartAt, DateTimeOffset? ProposedEndAt, int OfficeVersion);
public sealed record DriverTruckAssignment(Guid VehicleId, DateOnly From, DateOnly? To);

[QueryPolicy("shifts.enter_edit")]
public sealed record GetDriverShiftsQuery(Guid? SubjectId) : IQuery<ShiftSnapshot>, IHasSubject;

public static class ShiftViews
{
    public static ShiftView From(Shift shift) => new(shift.Id, shift.Date, shift.StartAt, shift.EndAt, shift.TruckId, shift.TrailerId,
        shift.Status, shift.Version, shift.RejectReason, shift.BlockedAtStart, shift.DeviceTimeGap,
        shift.Activities.OrderBy(activity => activity.StartAt).ThenBy(activity => activity.RecordedAt).Select(activity => new ActivityView(activity.Id, activity.Type, activity.StartAt, activity.EndAt)).ToArray(),
        shift.Overnights.OrderBy(overnight => overnight.Night).Select(overnight => new OvernightView(overnight.Id, overnight.Night, overnight.CountryCode, overnight.Kind)).ToArray())
        { SubmittedAt = shift.SubmittedAt, Source = shift.Source };
}

public interface IShiftCommandStore
{
    Task LockEmployeeAsync(Guid employeeId, CommandContext context, CancellationToken cancellationToken);
    Task<CommandReceipt?> ReceiptAsync(Guid employeeId, Guid commandId, CommandContext context, CancellationToken cancellationToken);
    Task<bool> HasFailedDependencyAsync(Guid employeeId, Guid aggregateId, CommandContext context, CancellationToken cancellationToken);
    Task<Shift?> LoadAsync(Guid employeeId, Guid shiftId, CommandContext context, CancellationToken cancellationToken);
    Task<bool> HasRunningShiftAsync(Guid employeeId, CommandContext context, CancellationToken cancellationToken);
    Task SaveAsync(Shift? shift, CommandReceipt receipt, ConflictRecord? conflict, CommandContext context, CancellationToken cancellationToken);
}

public sealed record ShiftStartFacts(bool Active, bool DriverCanDrive, Guid? SuggestedTruckId, bool TruckUsable, bool TrailerUsable);
public interface IShiftStartFacts
{
    Task<ShiftStartFacts> ReadAsync(Guid employeeId, Guid? truckId, Guid? trailerId, DateTimeOffset startAt, CommandContext context, CancellationToken cancellationToken);
}

public interface IShiftEvents
{
    Task ChangedAsync(Guid employeeId, Guid shiftId, string? eventCode, CommandContext context, CancellationToken cancellationToken);
}
