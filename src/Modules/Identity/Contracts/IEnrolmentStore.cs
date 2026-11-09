using FleetOps.Kernel.Authorization;

namespace FleetOps.Identity;

public interface IEnrolmentStore
{
    Task<EnrolmentEmployee?> LockEmployeeAsync(Guid id, CommandContext context, CancellationToken ct);
    Task<IReadOnlyList<EnrolmentCode>> ActiveCodesAsync(DateTimeOffset now, int maxTries, CommandContext context, CancellationToken ct);
    Task<EnrolmentEmployee?> FindEmployeeAsync(Guid id, CommandContext? context, CancellationToken ct);
    Task ReplaceCodeAsync(EnrolmentCode code, CommandContext context, CancellationToken ct);
    Task IncrementAttemptsAsync(Guid id, DateTimeOffset now, int maxTries, CommandContext context, CancellationToken ct);
    Task<bool> ConsumeAsync(Guid id, DateTimeOffset now, int maxTries, CommandContext context, CancellationToken ct);
    Task<Guid?> FindPasswordlessUserAsync(Guid employeeId, CommandContext context, CancellationToken ct);
    Task AddUserAsync(OfficeUser user, CommandContext context, CancellationToken ct);
    Task<bool> HasPrivacyAsync(Guid employeeId, CommandContext context, CancellationToken ct);
    Task AddSessionAsync(OfficeSession session, CommandContext context, CancellationToken ct);
}
