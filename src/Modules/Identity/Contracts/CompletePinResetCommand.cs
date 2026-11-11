using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
[CommandPolicy("driver.pin_reset.complete")]
public sealed record CompletePinResetCommand(Guid? SubjectId) : ICommand<AccessActionResult>, IHasSubject;
