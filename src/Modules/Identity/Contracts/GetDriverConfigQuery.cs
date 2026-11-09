using FleetOps.Kernel.Authorization;

namespace FleetOps.Identity;

[QueryPolicy("driver.config")]
public sealed record GetDriverConfigQuery(Guid? SubjectId) : IQuery<DriverConfig>, IHasSubject;
