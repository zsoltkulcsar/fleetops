using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[QueryPolicy("people.view")]
public sealed record GetPersonQuery(Guid? SubjectId) : IQuery<PersonDetail>, IHasSubject;
