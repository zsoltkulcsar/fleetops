using FleetOps.Kernel.Authorization;

namespace FleetOps.Identity;

[CommandPolicy("sessions.manage")]
public sealed record SendEnrolmentCommand(Guid? SubjectId) : ICommand<SendEnrolmentResult>, IHasSubject;
