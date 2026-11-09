using FleetOps.Kernel.Authorization;

namespace FleetOps.Identity;

[VisibleTo(RoleCode.Driver)]
public sealed record DriverConfig(int PinLength, int MaxPinTries, int MaxOfflineDays, string OfficePhone, string Name) : IReadModel;
