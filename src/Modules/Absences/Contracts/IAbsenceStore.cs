using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
public interface IAbsenceStore
{
    Task LockAsync(Guid employeeId, CommandContext context, CancellationToken cancellationToken);
    Task<Absence?> LoadAsync(Guid id, CommandContext? context, CancellationToken cancellationToken);
    Task<AbsenceFacts?> FactsAsync(Guid employeeId, CommandContext? context, CancellationToken cancellationToken);
    Task<IReadOnlyList<Absence>> ListAsync(DateOnly from, DateOnly to, Guid? employeeId, CommandContext? context, CancellationToken cancellationToken);
    Task<IReadOnlyList<AbsenceShift>> ShiftsAsync(Guid employeeId, DateOnly from, DateOnly to, CommandContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> OthersAsync(Absence absence, CommandContext? context, CancellationToken cancellationToken);
    Task SaveAsync(Absence absence, AccessContext actor, string reason, bool notifyDriver, bool notifyHr, CommandContext context, CancellationToken cancellationToken);
}
