using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;

namespace FleetOps.Modules.Fleet;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record DefectPhotoLink(Guid Id, ObjectLink Link);
