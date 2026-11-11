using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[QueryPolicy("people.view")]
public sealed record GetPersonHistoryQuery(Guid? SubjectId) : IQuery<PersonHistory>, IHasSubject;
