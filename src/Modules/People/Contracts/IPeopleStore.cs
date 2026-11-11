using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public interface IPeopleStore
{
    Task<Employee?> LockAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
    Task<Guid> DepotAsync(CommandContext context, CancellationToken cancellationToken);
    Task<bool> PersonnelNumberExistsAsync(string number, Guid exceptId, CommandContext context, CancellationToken cancellationToken);
    Task SaveAsync(Employee employee, bool create, IReadOnlyList<PersonAudit> audit, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<PersonFacts>> ReadAsync(DateOnly today, Guid? id, CommandContext? context, CancellationToken cancellationToken);
}
