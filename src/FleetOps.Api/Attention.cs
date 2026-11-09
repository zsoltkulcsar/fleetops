using FleetOps.Api.Pipeline;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Documents;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record AttentionItem(Guid Id, string RuleId, AlertSeverity Severity, string ScopeKey, DateOnly? DueAt, AlertStatus Status, bool Snoozable, int Version);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record AttentionGroup(string SubjectType, Guid SubjectId, string SubjectName, AlertSeverity WorstSeverity, IReadOnlyList<AttentionItem> Items);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record AttentionTotals(int Open, int Critical);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record AttentionResult(IReadOnlyList<AttentionGroup> Groups, AttentionTotals Totals, bool CompanyEmpty) : IReadModel;

[QueryPolicy("alerts.view")]
public sealed record GetAttentionQuery : IQuery<AttentionResult>;

[CommandPolicy("alerts.snooze", HasStatePrecondition = false)]
public sealed record SnoozeAlertCommand(Guid Id, int Version, DateTimeOffset Until, string Reason) : ICommand<AlertActionResult>, IHasSubject
{
    public Guid? SubjectId => Id;
}

[CommandPolicy("alerts.snooze")]
public sealed record UnsnoozeAlertCommand(Guid Id, int Version) : ICommand<AlertActionResult>, IHasSubject
{
    public Guid? SubjectId => Id;
}

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record AlertActionResult(Guid Id, AlertStatus Status, int Version) : IReadModel;

public sealed class AlertValidationException(IReadOnlyList<string> fields) : Exception("validation_error")
{
    public IReadOnlyList<string> Fields { get; } = fields;
}

public sealed class AlertConflictException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public static class AlertVisibility
{
    private static readonly IReadOnlySet<string> HrRules = new HashSet<string>(StringComparer.Ordinal) { "A-01", "A-02", "A-03", "A-10", "A-13", "A-14", "A-17", "A-18" };

    public static bool CanSee(string ruleId, string? subjectType, RoleCode role) => role switch
    {
        RoleCode.Owner => true,
        RoleCode.Hr => HrRules.Contains(ruleId) && (ruleId != "A-03" || subjectType == "employee"),
        RoleCode.Dispatcher => (ruleId == "A-05" && subjectType == "vehicle") || (ruleId == "A-03" && subjectType == "vehicle") || (ruleId == "A-21" && subjectType == "cycle"),
        _ => false,
    };
}

public sealed class AttentionQueryHandler(IConfiguration configuration, TimeProvider time) : IQueryHandler<GetAttentionQuery, AttentionResult>
{
    public async Task<AttentionResult> HandleAsync(GetAttentionQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.")).Options;
        await using var db = new FleetOpsDbContext(options);
        var roles = actor.Roles.ToArray();
        var now = time.GetUtcNow();
        var visible = db.Alerts.AsNoTracking().Where(x => x.Status != AlertStatus.Resolved && (x.Status != AlertStatus.Snoozed || x.SnoozedUntil <= now || x.SnoozedUntil == null));
        if (!roles.Contains(RoleCode.Owner))
        {
            var hr = roles.Contains(RoleCode.Hr);
            var dispatcher = roles.Contains(RoleCode.Dispatcher);
            visible = visible.Where(x =>
                (hr && ((x.RuleId == "A-01") || (x.RuleId == "A-02") || (x.RuleId == "A-03" && db.Documents.Where(d => d.Id == x.SubjectId).Select(d => d.OwnerType).FirstOrDefault() == DocumentOwnerType.Employee) || x.RuleId == "A-10" || x.RuleId == "A-13" || x.RuleId == "A-14" || x.RuleId == "A-17" || x.RuleId == "A-18")) ||
                (dispatcher && (x.RuleId == "A-05" || (x.RuleId == "A-21" && x.SubjectType == "cycle") || (x.RuleId == "A-03" && db.Documents.Where(d => d.Id == x.SubjectId).Select(d => d.OwnerType).FirstOrDefault() == DocumentOwnerType.Vehicle))));
        }
        var rows = await visible.Select(x => new
        {
            Alert = x,
            SubjectType = x.SubjectType == "document" ? db.Documents.Where(d => d.Id == x.SubjectId).Select(d => d.OwnerType == DocumentOwnerType.Employee ? "employee" : "vehicle").FirstOrDefault() : x.SubjectType,
            SubjectId = x.SubjectType == "document" ? db.Documents.Where(d => d.Id == x.SubjectId).Select(d => (Guid?)d.OwnerId).FirstOrDefault() : x.SubjectId,
            SubjectName = x.SubjectType == "document"
                ? db.Documents.Where(d => d.Id == x.SubjectId).Select(d => d.OwnerType == DocumentOwnerType.Employee ? db.Employees.Where(e => e.Id == d.OwnerId).Select(e => e.FirstName + " " + e.LastName).FirstOrDefault() : db.Vehicles.Where(v => v.Id == d.OwnerId).Select(v => v.Plate).FirstOrDefault()).FirstOrDefault()
                : x.SubjectType == "cycle" ? db.Cycles.Where(cycle => cycle.Id == x.SubjectId).Select(cycle => db.Vehicles.Where(vehicle => vehicle.Id == cycle.VehicleId).Select(vehicle => vehicle.Plate).FirstOrDefault() + " / " + db.Employees.Where(employee => employee.Id == cycle.EmployeeId).Select(employee => employee.FirstName + " " + employee.LastName).FirstOrDefault()).FirstOrDefault()
                : x.SubjectType == "employee" ? db.Employees.Where(e => e.Id == x.SubjectId).Select(e => e.FirstName + " " + e.LastName).FirstOrDefault() : db.Vehicles.Where(v => v.Id == x.SubjectId).Select(v => v.Plate).FirstOrDefault(),
        }).ToListAsync(cancellationToken);
        var groups = rows.Where(x => x.SubjectId.HasValue && x.SubjectName != null).GroupBy(x => new { x.SubjectType, Id = x.SubjectId!.Value, Name = x.SubjectName! }).Select(group => new AttentionGroup(group.Key.SubjectType!, group.Key.Id, group.Key.Name, group.Max(x => x.Alert.Severity), group.OrderByDescending(x => x.Alert.Severity).ThenBy(x => x.Alert.DueAt).Select(x => new AttentionItem(x.Alert.Id, x.Alert.RuleId, x.Alert.Severity, x.Alert.ScopeKey, x.Alert.DueAt, x.Alert.Status, x.Alert.Severity != AlertSeverity.Critical, x.Alert.Version)).ToList())).OrderByDescending(x => x.WorstSeverity).ThenBy(x => x.Items.Min(y => y.DueAt)).ThenBy(x => x.SubjectName).ToList();
        var employeeExists = await db.Employees.AnyAsync(x => x.Roles.Contains("driver"), cancellationToken);
        var vehicleExists = await db.Vehicles.AnyAsync(cancellationToken);
        return new(groups, new(rows.Count, rows.Count(x => x.Alert.Severity == AlertSeverity.Critical)), !employeeExists && !vehicleExists);
    }
}

public sealed class AlertCommandHandler(TimeProvider time) : ICommandHandler<SnoozeAlertCommand, AlertActionResult>, ICommandHandler<UnsnoozeAlertCommand, AlertActionResult>
{
    public Task<AlertActionResult> HandleAsync(SnoozeAlertCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken) => UpdateAsync(command.Id, command.Version, actor, context, cancellationToken, command.Until, command.Reason);
    public Task<AlertActionResult> HandleAsync(UnsnoozeAlertCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken) => UpdateAsync(command.Id, command.Version, actor, context, cancellationToken, null, null);

    private async Task<AlertActionResult> UpdateAsync(Guid id, int version, AccessContext actor, CommandContext context, CancellationToken cancellationToken, DateTimeOffset? until, string? reason)
    {
        var now = time.GetUtcNow();
        if (until is not null && (until <= now || until > now.AddDays(7))) throw new AlertValidationException(["until"]);
        if (until is not null && string.IsNullOrWhiteSpace(reason)) throw new AlertValidationException(["reason"]);
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        await using var db = new FleetOpsDbContext(options.Options) { AuditActorId = actor.EmployeeId, AuditReason = reason?.Trim() ?? "unsnooze" };
        db.Database.UseTransaction(context.DbTransaction);
        var alert = await db.Alerts.FromSqlInterpolated($"select * from fleetops.alerts where id = {id} for update").SingleOrDefaultAsync(cancellationToken) ?? throw new AlertConflictException("not_found");
        var subjectType = alert.SubjectType;
        if (alert.SubjectType == "document") subjectType = await db.Documents.Where(x => x.Id == alert.SubjectId).Select(x => x.OwnerType == DocumentOwnerType.Employee ? "employee" : "vehicle").SingleOrDefaultAsync(cancellationToken);
        if (!actor.Roles.Any(role => AlertVisibility.CanSee(alert.RuleId, subjectType, role))) throw new AlertConflictException("not_found");
        if (alert.Version != version) throw new AlertConflictException("version_conflict");
        if (alert.Status == AlertStatus.Resolved) throw new AlertConflictException("state_conflict");
        if (until is not null && alert.Severity == AlertSeverity.Critical) throw new AlertConflictException("not_snoozable");
        alert.Status = until is null ? AlertStatus.Open : AlertStatus.Snoozed;
        alert.SnoozedUntil = until;
        alert.SnoozeReason = reason?.Trim();
        alert.Version++;
        db.AuditEntries.Add(new FleetOps.Modules.Audit.AuditEntry { Id = Guid.CreateVersion7(), EntityType = nameof(Alert), EntityId = id, FieldName = until is null ? "unsnoozed" : "snoozed", ActorId = actor.EmployeeId.ToString(), Reason = reason?.Trim() ?? "unsnooze", CreatedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        return new(alert.Id, alert.Status, alert.Version);
    }
}
