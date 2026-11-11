using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Shifts;

[QueryPolicy("shifts.approve_reject")]
public sealed record GetWeekGridQuery(DateOnly Week, string Filter = "all", string? Search = null, int Page = 1, int PageSize = 50) : IQuery<WeekGrid>;

[QueryPolicy("shifts.approve_reject")]
public sealed record GetShiftDetailQuery(Guid ShiftId) : IQuery<ShiftDetail>;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record WeekGrid(DateOnly Week, string CompanyTimeZone, int Page, int PageSize, int Total,
    GridCounts Counts, IReadOnlyList<WeekRow> Rows, CutoffBanner? Cutoff) : IReadModel;
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record GridCounts(int All, int Awaiting, int Missing, int Warnings);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record CutoffBanner(DateOnly Date, int AwaitingShifts);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record WeekRow(Guid EmployeeId, string Driver, string? PersonnelNumber, IReadOnlyList<WeekCell> Days,
    int WorkMinutes, IReadOnlyList<NightCount> Nights, string Status, bool Warnings)
{
    public IReadOnlyList<RuleWarning> RuleWarnings { get; init; } = [];
}
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record NightCount(string CountryCode, int Count);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record WeekCell(DateOnly Date, int WorkMinutes, string Status, string Label, bool Missing, bool Warnings,
    IReadOnlyList<GridShift> Shifts, IReadOnlyList<string> Absences);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record GridShift(Guid Id, int Version, ShiftStatus Status, bool Flagged)
{
    public IReadOnlyList<RuleWarning> RuleWarnings { get; init; } = [];
}

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ShiftDetail(Guid EmployeeId, string Driver, string? TruckPlate, OfficeShiftView Shift,
    IReadOnlyList<string> Flags, IReadOnlyList<ShiftConflictView> Conflicts, IReadOnlyList<ShiftHistory> History) : IReadModel
{
    public IReadOnlyList<RuleWarning> RuleWarnings { get; init; } = [];
    public IReadOnlyList<CorrectionView> Corrections { get; init; } = [];
}
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record OfficeShiftView(Guid Id, DateOnly Date, DateTimeOffset StartAt, DateTimeOffset? EndAt, Guid? TruckId,
    Guid? TrailerId, ShiftStatus Status, ShiftSource Source, int Version, bool? DepartureCheckOk,
    int? OdometerStartKm, int? OdometerEndKm, DateTimeOffset? SubmittedAt, Guid? ApprovedBy, DateTimeOffset? ApprovedAt,
    string? ApprovalNote, string? RejectReason, IReadOnlyList<ActivityView> Activities, IReadOnlyList<OvernightView> Overnights,
    decimal DrivingHours, decimal OtherWorkHours, decimal BreakHours, decimal AvailabilityHours);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ShiftConflictView(Guid Id, Guid CommandId, int BaseVersion, int OfficeVersion, string DriverPayload, DateTimeOffset CreatedAt);
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ShiftHistory(Guid AuditId, string EntityType, Guid EntityId, string Field, string? Before, string? After,
    string Editor, string Reason, DateTimeOffset At);

[CommandPolicy("shifts.approve_reject", Separation = SeparationOfDuty.OwnerRoutesToHr)]
public sealed record ApproveShiftCommand(Guid ShiftId, Guid? SubjectId, int Version, string? Note) : ICommand<OfficeShiftResult>, IHasSubject;
[CommandPolicy("shifts.approve_reject", Separation = SeparationOfDuty.OwnerRoutesToHr)]
public sealed record RejectShiftCommand(Guid ShiftId, Guid? SubjectId, int Version, string Reason) : ICommand<OfficeShiftResult>, IHasSubject;
[CommandPolicy("shifts.enter_edit", HasStatePrecondition = true)]
public sealed record EditOfficeShiftCommand(Guid ShiftId, Guid? SubjectId, int Version, string Reason, OfficeShiftInput Input) : ICommand<OfficeShiftResult>, IHasSubject;
[CommandPolicy("shifts.enter_edit", HasStatePrecondition = true)]
public sealed record AddOfficeShiftCommand(Guid? SubjectId, string Reason, OfficeShiftInput Input) : ICommand<OfficeShiftResult>, IHasSubject;
[CommandPolicy("shifts.enter_edit", HasStatePrecondition = true)]
public sealed record EndOfficeShiftCommand(Guid ShiftId, Guid? SubjectId, int Version, string Reason, DateTimeOffset EndAt, int? OdometerEndKm) : ICommand<OfficeShiftResult>, IHasSubject;

public sealed record OfficeShiftInput(DateOnly Date, DateTimeOffset StartAt, DateTimeOffset? EndAt, Guid? TruckId,
    Guid? TrailerId, int? OdometerStartKm, int? OdometerEndKm, IReadOnlyList<ActivityView>? Activities, IReadOnlyList<OvernightView>? Overnights);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record OfficeShiftResult(Guid ShiftId, int Version, ShiftStatus Status) : IReadModel
{
    public IReadOnlyList<RuleWarning> RuleWarnings { get; init; } = [];
}
public sealed class TimesheetValidationException(params string[] fields) : Exception("validation_error")
{
    public IReadOnlyList<string> Fields { get; } = fields;
}

public interface IOfficeShiftStore
{
    Task<Shift?> LoadAsync(Guid employeeId, Guid shiftId, CommandContext context, CancellationToken cancellationToken);
    Task<bool> IsFlaggedAsync(Shift shift, CommandContext context, CancellationToken cancellationToken);
    Task ValidateReferencesAsync(Shift shift, CommandContext context, CancellationToken cancellationToken);
    Task<Guid> SaveAsync(Shift shift, Guid actorId, string reason, CommandContext context, CancellationToken cancellationToken);
    Task NotifyAsync(Guid employeeId, Guid shiftId, Guid auditId, string template, CommandContext context, CancellationToken cancellationToken);
}

public interface ITimesheetReadStore
{
    string CompanyTimeZone { get; }
    Task<IReadOnlyList<WeekDriverFacts>> WeekAsync(DateOnly week, string? search, CancellationToken cancellationToken);
    Task<ShiftDetail?> DetailAsync(Guid shiftId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<int> AwaitingPeriodAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
public sealed record WeekDriverFacts(Guid EmployeeId, string Driver, string? PersonnelNumber, IReadOnlyList<Shift> Shifts,
    IReadOnlySet<DateOnly> CycleDates, IReadOnlyList<WeekAbsence> Absences, IReadOnlySet<Guid> FlaggedShifts)
{
    public IReadOnlyList<Shift> GuidanceHistory { get; init; } = [];
}
public sealed record WeekAbsence(DateOnly From, DateOnly To, string Type);
