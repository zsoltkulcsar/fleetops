using FleetOps.Kernel.Authorization;

namespace FleetOps.Kernel;

public enum ReadinessShownStatus { Active, OffRoad, InMaintenance, Retired }
public enum ReadinessCauseCode { DocumentExpired, DocumentMissing, ManualOffRoad, CriticalDefect, Retired, Suspended, Left, NotADriver, InWorkshop }

[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ReadinessCause(ReadinessCauseCode Code, string? DocumentType);

[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record VehicleReadiness(bool Usable, ReadinessShownStatus ShownStatus, IReadOnlyList<ReadinessCause> Causes);

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DriverReadiness(bool CanDrive, IReadOnlyList<ReadinessCause> Causes);
