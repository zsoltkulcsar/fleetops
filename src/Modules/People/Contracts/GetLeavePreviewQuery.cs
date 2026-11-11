using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
[QueryPolicy("people.manage")]
public sealed record GetLeavePreviewQuery(Guid? SubjectId, DateOnly? EndDate) : IQuery<LeavePreview>, IHasSubject;
