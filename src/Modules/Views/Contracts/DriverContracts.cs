using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Schedule;

namespace FleetOps.Modules.Views;

// AD-11 allow-list: dates/truck, own document type/status/expiry/reject reason, derived readiness.
// File bytes, object keys, signed links, reporter and other employees never enter this snapshot.
[VisibleTo(RoleCode.Driver)]
public sealed record DriverSnapshot(Guid EmployeeId, DateTimeOffset GeneratedAt, IReadOnlyList<DriverCycle> Cycles, IReadOnlyList<DriverDocument> Documents, bool CanDrive, IReadOnlyList<ReadinessCause> ReadinessCauses) : IReadModel
{
    public IReadOnlyList<DriverSwap> Swaps { get; init; } = [];
    public IReadOnlyList<DriverOwnDefect> Defects { get; init; } = [];
}
public sealed record DriverCycle(Guid Id, Guid VehicleId, string VehiclePlate, DateOnly From, DateOnly To, CycleStatus Status, CycleSource Source, int Version);
public sealed record DriverDocument(Guid Id, DocumentType Type, DocumentStatus Status, DateOnly? ExpiresAt, string? RejectReason);
public sealed record DriverVehicleItem(Guid Id, string Category, string Severity, DateOnly Date, Guid? DocumentId, Guid? FileId);
public sealed record DriverVehicleDocument(Guid Id, DocumentType Type, DocumentStatus Status, DateOnly? ExpiresAt, IReadOnlyList<DriverFile> Files);
[VisibleTo(RoleCode.Driver)]
public sealed record DriverVehicleDetail(Guid Id, string Plate, IReadOnlyList<DriverVehicleItem> Items, IReadOnlyList<DriverVehicleDocument> Documents) : IReadModel
{
    public IReadOnlyList<DriverVehicleDefect> Defects { get; init; } = [];
}
[VisibleTo(RoleCode.Driver)]
public sealed record DriverFileLinkResult(string Url, DateTimeOffset ExpiresAt) : IReadModel;

[QueryPolicy("schedule.view")]
public sealed record GetDriverSnapshotQuery(Guid? SubjectId) : IQuery<DriverSnapshot>, IHasSubject;
[QueryPolicy("schedule.view")]
public sealed record GetDriverVehicleQuery(Guid? SubjectId, Guid VehicleId) : IQuery<DriverVehicleDetail>, IHasSubject;
[QueryPolicy("schedule.view")]
public sealed record GetDriverDocumentQuery(Guid? SubjectId, Guid DocumentId) : IQuery<DriverDocumentDetail>, IHasSubject;
[QueryPolicy("schedule.view")]
public sealed record GetDriverFileLinkQuery(Guid? SubjectId, Guid DocumentId, Guid FileId) : IQuery<DriverFileLinkResult>, IHasSubject;


[VisibleTo(RoleCode.Driver)]
public sealed record DriverOwnDefect(Guid Id, Guid VehicleId, string Category, string Severity, string Status, DateTimeOffset ReportedAt);
[VisibleTo(RoleCode.Driver)]
public sealed record DriverVehicleDefect(Guid Id, string Category, string Severity, DateOnly Date, IReadOnlyList<DriverDefectPhoto> Photos);
[VisibleTo(RoleCode.Driver)]
public sealed record DriverDefectPhoto(Guid Id, string Link);
[QueryPolicy("schedule.view")]
public sealed record GetDriverDefectPhotoLinkQuery(Guid? SubjectId, Guid DefectId, Guid FileId) : IQuery<DriverFileLinkResult>, IHasSubject;
