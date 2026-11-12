using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Notifications;
public enum NotificationJobKind { Digest, Escalation }
[CommandPolicy("jobs.trigger")]
public sealed record RunNotificationJobCommand(NotificationJobKind Kind, string? Cursor) : ICommand<NotificationJobResult>;
[VisibleTo(RoleCode.Owner)]
public sealed record NotificationJobResult(bool Done, string? Cursor, int Processed) : IReadModel;
public sealed record EscalationCandidate(Guid Id, string RuleId, string AssignedRole);
public sealed record DigestAlert(Guid Id, string RuleId);
public interface INotificationJobStore
{
    Task<IReadOnlyList<EscalationCandidate>> LockEscalationsAsync(DateTimeOffset cutoff, string? cursor, CommandContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> OfficeRecipientsAsync(string[] roles, CommandContext context, CancellationToken cancellationToken);
    Task MarkEscalatedAsync(Guid id, DateTimeOffset now, CommandContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> DigestUsersAsync(string? cursor, CommandContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<DigestAlert>> DigestAlertsAsync(Guid employeeId, DateOnly today, CommandContext context, CancellationToken cancellationToken);
    Task QueueDigestAsync(Guid employeeId, DateOnly today, object parameters, CommandContext context, CancellationToken cancellationToken);
}
