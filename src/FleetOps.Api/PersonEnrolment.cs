using FleetOps.Identity;
using FleetOps.Kernel.Authorization;
using FleetOps.People;

namespace FleetOps.Api;

public sealed class PersonEnrolment(ICommandHandler<SendEnrolmentCommand, SendEnrolmentResult> handler) : IPersonEnrolment
{
    public async Task<bool> SendAsync(Guid id, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await context.DbTransaction.SaveAsync("person_enrolment", cancellationToken);
        try
        {
            await handler.HandleAsync(new(id), actor, context, cancellationToken);
            await context.DbTransaction.ReleaseAsync("person_enrolment", cancellationToken);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await context.DbTransaction.RollbackAsync("person_enrolment", cancellationToken);
            return false;
        }
    }
}
