using System.Globalization;
using System.Security.Cryptography;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;
using Microsoft.Extensions.Options;
namespace FleetOps.Identity;
public sealed class SendEnrolmentHandler(IEnrolmentStore store, ISettings settings, IEnrolmentSms sms, IOptions<EnrolmentOptions> options, TimeProvider time, ICompanyTimeService clock) : ICommandHandler<SendEnrolmentCommand, SendEnrolmentResult>
{
    public async Task<SendEnrolmentResult> HandleAsync(SendEnrolmentCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var employee = await store.LockEmployeeAsync(command.SubjectId ?? Guid.Empty, context, cancellationToken);
        var now = time.GetUtcNow();
        if (!EnrolmentRules.Eligible(employee, now, clock) || string.IsNullOrWhiteSpace(employee!.PersonnelNumber) || string.IsNullOrWhiteSpace(employee.Phone))
            throw new EnrolmentNotPossibleException();
        var minutes = await EnrolmentRules.PositiveSetting(settings, "enrolment.code_minutes", context, cancellationToken);
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        await store.ReplaceCodeAsync(new EnrolmentCode
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            CodeHash = SessionTokens.Hash($"{employee.Id}:{code}"),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(minutes),
            IssuedBy = actor.EmployeeId
        }, context, cancellationToken);
        await sms.SendAsync(employee.Phone!, code, cancellationToken);
        return new(now, options.Value.TestInbox ? new(code, employee.PersonnelNumber!) : null);
    }
}
