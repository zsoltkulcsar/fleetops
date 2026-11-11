using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
[QueryPolicy("sessions.view")]
public sealed record GetPersonSessionsQuery(Guid PersonId) : IQuery<PersonSessions>;
