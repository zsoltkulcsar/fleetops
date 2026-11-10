using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Readiness;
using FleetOps.Modules.Setup;

namespace FleetOps.Modules.Alerts;

public enum AlertSeverity { Info, Warning, Critical }
public enum AlertStatus { Open, Snoozed, Resolved }

public sealed class Alert
{
    [FleetOps.Modules.Audit.NotAudited] public Guid Id { get; set; } = Guid.CreateVersion7();
    public string RuleId { get; set; } = "";
    public string SubjectType { get; set; } = "";
    public Guid SubjectId { get; set; }
    public string ScopeKey { get; set; } = "";
    public AlertSeverity Severity { get; set; }
    public DateOnly? DueAt { get; set; }
    public string AssignedRole { get; set; } = "";
    public AlertStatus Status { get; set; } = AlertStatus.Open;
    public DateTimeOffset? SnoozedUntil { get; set; }
    public string? SnoozeReason { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EscalatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed record AlertCondition(string ScopeKey, AlertSeverity Severity, DateOnly? DueAt);
public sealed record EvaluationContext(CommandContext Command, ISettings Settings, IReadiness Readiness);

public interface IAlertEvaluator
{
    string RuleId { get; }
    string SubjectType { get; }
    Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken);
}

public interface IAlertEngine
{
    Task ApplyAsync(string subjectType, Guid subjectId, CommandContext context, CancellationToken cancellationToken);
}

public interface IAlertOpened
{
    Task HandleAsync(string ruleId, string subjectType, Guid subjectId, Guid alertId, CommandContext context, CancellationToken cancellationToken);
}
