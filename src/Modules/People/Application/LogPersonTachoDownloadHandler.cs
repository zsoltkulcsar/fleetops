using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.People;

public sealed class LogPersonTachoDownloadHandler(IPersonTachoDownloads store, ICompanyTimeService companyTime, TimeProvider time, IChangedSubjects changed)
    : ICommandHandler<LogPersonTachoDownloadCommand, PersonTachoDownloadResult>
{
    public async Task<PersonTachoDownloadResult> HandleAsync(LogPersonTachoDownloadCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (command.Id == Guid.Empty || command.Date == default || command.Date > companyTime.ToCompanyDate(time.GetUtcNow()))
            throw new PersonValidationException(["date", "id"]);
        var person = await store.LockAsync(command.Id, context, cancellationToken);
        if (person is null || person.Status != EmployeeStatus.Active || !(System.Text.Json.JsonSerializer.Deserialize<string[]>(person.Roles) ?? []).Contains("driver", StringComparer.OrdinalIgnoreCase))
            throw new PersonValidationException(["id"]);
        person.LastTachoDownloadAt = command.Date;
        await store.SaveAsync(person, actor.EmployeeId, context, cancellationToken);
        changed.Add("employee", command.Id);
        return new(command.Id, command.Date);
    }
}
