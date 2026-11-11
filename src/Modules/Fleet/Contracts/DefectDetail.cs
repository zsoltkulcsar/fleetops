using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record DefectDetail(DefectRow Report, string? Description, Guid? RepairId, Guid? ClosedBy, DateTimeOffset? ClosedAt,
    string? CloseReason, IReadOnlyList<DefectPhotoLink> Photos, IReadOnlyList<DefectHistoryItem> History) : IReadModel;
