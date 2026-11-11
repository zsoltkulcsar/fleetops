using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
public sealed class PersonAccessHandler(IPersonAccessStore store, IEnrolmentSms sms, ICurrentSession session, IOptions<EnrolmentOptions> options) :
    IQueryHandler<GetPersonSessionsQuery, PersonSessions>, ICommandHandler<RevokePersonSessionCommand, AccessActionResult>,
    ICommandHandler<ResetPersonPinCommand, AccessActionResult>, ICommandHandler<CompletePinResetCommand, AccessActionResult>,
    ICommandHandler<ChangePersonPhoneCommand, PhoneChangeResult>, ICommandHandler<SetPersonRolesCommand, AccessActionResult>,
    ICommandHandler<InviteOfficePersonCommand, AccessActionResult>
{
    public Task<PersonSessions> HandleAsync(GetPersonSessionsQuery query, AccessContext actor, CancellationToken cancellationToken) => store.SessionsAsync(query.PersonId, cancellationToken);
    public async Task<AccessActionResult> HandleAsync(RevokePersonSessionCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        ValidateReason(command.Reason);
        await Person(command.PersonId, context, cancellationToken);
        await store.RevokeAsync(command.PersonId, command.SessionId, command.Reason!.Trim(), actor, context, cancellationToken);
        return new("session_revoked");
    }
    public async Task<AccessActionResult> HandleAsync(ResetPersonPinCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var person = await Person(command.PersonId, context, cancellationToken);
        if (!person.Roles.Contains("driver")) throw new AccessConflictException("state_conflict");
        await store.ResetPinAsync(person.Id, actor, context, cancellationToken);
        return new("pin_reset_requested");
    }
    public async Task<AccessActionResult> HandleAsync(CompletePinResetCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var sessionId = session.SessionId ?? throw new AuthorizationException("not_signed_in", "driver.pin_reset.complete", []);
        await Person(actor.EmployeeId, context, cancellationToken);
        await store.CompletePinResetAsync(actor.EmployeeId, sessionId, actor, context, cancellationToken);
        return new("pin_reset_completed");
    }
    public async Task<PhoneChangeResult> HandleAsync(ChangePersonPhoneCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        ValidateReason(command.Reason);
        if (command.Phone is null || !Regex.IsMatch(command.Phone, @"^\+[1-9][0-9]{1,14}$")) throw new AccessValidationException(["phone"]);
        var person = await Person(command.PersonId, context, cancellationToken);
        if (!person.Roles.Contains("driver") || string.IsNullOrWhiteSpace(person.Phone)) throw new AccessConflictException("state_conflict");
        if (person.Phone == command.Phone) return new("phone_unchanged");
        await store.ChangePhoneAsync(person.Id, command.Phone, command.Reason!.Trim(), actor, context, cancellationToken);
        const string message = "Your FleetOps phone number was changed. If this was not you, call HR.";
        await sms.SendAsync(person.Phone, message, cancellationToken);
        return new("phone_changed") { TestInbox = options.Value.TestInbox ? new(person.Phone, message) : null };
    }
    public async Task<AccessActionResult> HandleAsync(SetPersonRolesCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        ValidateReason(command.Reason);
        string[] allowed = ["driver", "dispatcher", "hr", "owner"];
        if (command.Roles is null || command.Roles.Length == 0 || command.Roles.Any(role => !allowed.Contains(role)) || command.Roles.Distinct().Count() != command.Roles.Length)
            throw new AccessValidationException(["roles"]);
        var person = await Person(command.PersonId, context, cancellationToken);
        if (person.Roles.Contains("owner") != command.Roles.Contains("owner"))
        {
            if (!actor.Roles.Contains(RoleCode.Owner)) throw new AuthorizationException("permission_denied", "people.roles", [RoleCode.Owner]);
            if (person.Roles.Contains("owner") && await store.OwnersAsync(context, cancellationToken) <= 1) throw new AccessConflictException("last_owner");
        }
        if (command.Roles.Contains("driver") && string.IsNullOrWhiteSpace(person.PersonnelNumber)) throw new AccessValidationException(["personnelNumber"]);
        if (command.Roles.Any(role => role != "driver") && (string.IsNullOrWhiteSpace(person.Email) || !new EmailAddressAttribute().IsValid(person.Email))) throw new AccessValidationException(["email"]);
        await store.SetRolesAsync(person.Id, command.Roles, !command.Roles.Any(role => role is "dispatcher" or "hr" or "owner"), command.Reason!.Trim(), actor, context, cancellationToken);
        return new("roles_updated");
    }
    public async Task<AccessActionResult> HandleAsync(InviteOfficePersonCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var person = await Person(command.PersonId, context, cancellationToken);
        if (!person.Roles.Any(role => role is "dispatcher" or "hr" or "owner") || string.IsNullOrWhiteSpace(person.Email) || !new EmailAddressAttribute().IsValid(person.Email)) throw new AccessValidationException(["email"]);
        await store.InviteAsync(person, actor, context, cancellationToken);
        return new("invite_sent");
    }
    private async Task<AccessPerson> Person(Guid id, CommandContext context, CancellationToken cancellationToken) =>
        await store.LockPersonAsync(id, context, cancellationToken) ?? throw new AccessConflictException("not_found");
    private static void ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500 || reason.Any(char.IsControl)) throw new AccessValidationException(["reason"]);
    }
}
