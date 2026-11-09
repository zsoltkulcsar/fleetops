using System.Text.Json;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Readiness;
using FleetOps.Modules.Setup;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using FleetOps.Modules.Notifications;
using FleetOps.Modules.Schedule;

namespace FleetOps.Migrations;

public sealed class AlertEngine(IEnumerable<IAlertEvaluator> evaluators, ISettings settings, IReadiness readiness, ICompanyTimeService companyTime, TimeProvider time, ILogger<AlertEngine> logger, INotifications notifications) : IAlertEngine
{
    public async Task ApplyAsync(string subjectType, Guid subjectId, CommandContext context, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var today = companyTime.ToCompanyDate(now);
        var evaluation = new EvaluationContext(context, settings, readiness);
        foreach (var evaluator in evaluators.Where(x => x.SubjectType == subjectType))
        {
            await using var savepoint = new NpgsqlCommand("savepoint alert_evaluator", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
            await savepoint.ExecuteNonQueryAsync(cancellationToken);
            try
            {
                var conditions = await evaluator.EvaluateAsync(subjectId, today, now, evaluation, cancellationToken);
                await ApplyRuleAsync(evaluator, subjectId, conditions, now, context, notifications, cancellationToken);
            }
            catch (Exception exception)
            {
                await using var rollback = new NpgsqlCommand("rollback to savepoint alert_evaluator", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
                await rollback.ExecuteNonQueryAsync(CancellationToken.None);
                logger.LogError("Alert evaluator failed. ExceptionType={ExceptionType} RuleId={RuleId}", exception.GetType().Name, evaluator.RuleId);
            }
        }
    }

    private static async Task ApplyRuleAsync(IAlertEvaluator evaluator, Guid subjectId, IReadOnlyList<AlertCondition> conditions, DateTimeOffset now, CommandContext context, INotifications notifications, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
        var scopes = conditions.Select(x => x.ScopeKey).Distinct(StringComparer.Ordinal).ToArray();
        command.CommandText = "update fleetops.alerts set status = 'Resolved', resolved_at = $1, resolved_by = null, version = version + 1 where rule_id = $2 and subject_type = $3 and subject_id = $4 and status <> 'Resolved' and (cardinality($5::text[]) = 0 or not (scope_key = any($5::text[])))";
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(evaluator.RuleId);
        command.Parameters.AddWithValue(evaluator.SubjectType);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(scopes);
        await command.ExecuteNonQueryAsync(cancellationToken);
        var assignedRole = evaluator.RuleId is "A-05" or "A-21" ? "dispatcher" : "hr";
        if (evaluator.RuleId == "A-03" && evaluator.SubjectType == "document")
        {
            await using var ownerTypeCommand = new NpgsqlCommand("select owner_type from fleetops.documents where id = $1", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
            ownerTypeCommand.Parameters.AddWithValue(subjectId);
            var ownerType = (string?)await ownerTypeCommand.ExecuteScalarAsync(cancellationToken);
            assignedRole = string.Equals(ownerType, nameof(DocumentOwnerType.Vehicle), StringComparison.OrdinalIgnoreCase) ? "dispatcher" : "hr";
        }
        foreach (var condition in conditions)
        {
            var alertId = Guid.CreateVersion7();
            await using var insert = new NpgsqlCommand("insert into fleetops.alerts (id, rule_id, subject_type, subject_id, scope_key, severity, due_at, assigned_role, status, opened_at, version) values ($1,$2,$3,$4,$5,$6,$7,$8,'Open',$9,1) on conflict (rule_id, subject_type, subject_id, scope_key) where status <> 'Resolved' do nothing returning id", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
            insert.Parameters.AddWithValue(alertId);
            insert.Parameters.AddWithValue(evaluator.RuleId);
            insert.Parameters.AddWithValue(evaluator.SubjectType);
            insert.Parameters.AddWithValue(subjectId);
            insert.Parameters.AddWithValue(condition.ScopeKey);
            insert.Parameters.AddWithValue(condition.Severity.ToString());
            insert.Parameters.AddWithValue(condition.DueAt is null ? DBNull.Value : condition.DueAt.Value);
            insert.Parameters.AddWithValue(assignedRole);
            insert.Parameters.AddWithValue(now);
            var inserted = await insert.ExecuteScalarAsync(cancellationToken);
            if (inserted is Guid insertedAlertId && condition.Severity == AlertSeverity.Critical)
            {
                await using var employees = new NpgsqlCommand("select id from fleetops.employees where status = 'Active' and roles::jsonb @> $1::jsonb", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
                employees.Parameters.AddWithValue(JsonSerializer.Serialize(new[] { assignedRole }));
                await using var reader = await employees.ExecuteReaderAsync(cancellationToken);
                var employeeIds = new List<Guid>();
                while (await reader.ReadAsync(cancellationToken)) employeeIds.Add(reader.GetGuid(0));
                await reader.CloseAsync();
                foreach (var employeeId in employeeIds)
                    await notifications.AddAsync(employeeId, "critical_alert", new { alertId = insertedAlertId, ruleId = evaluator.RuleId, subjectType = evaluator.SubjectType, subjectId, documentType = evaluator.RuleId == "A-02" ? condition.ScopeKey : null }, insertedAlertId, context, cancellationToken);
            }
            await using var update = new NpgsqlCommand("update fleetops.alerts set severity = $1, due_at = $2, version = version + 1 where rule_id = $3 and subject_type = $4 and subject_id = $5 and scope_key = $6 and status <> 'Resolved' and (severity <> $1 or due_at is distinct from $2)", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
            update.Parameters.AddWithValue(condition.Severity.ToString()); update.Parameters.AddWithValue(condition.DueAt is null ? DBNull.Value : condition.DueAt.Value); update.Parameters.AddWithValue(evaluator.RuleId); update.Parameters.AddWithValue(evaluator.SubjectType); update.Parameters.AddWithValue(subjectId); update.Parameters.AddWithValue(condition.ScopeKey);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

public sealed class A02EmployeeEvaluator : IAlertEvaluator
{
    public string RuleId => "A-02";
    public string SubjectType => "employee";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context.Command);
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == subjectId, cancellationToken);
        if (employee is null || employee.Status != EmployeeStatus.Active || !JsonSerializer.Deserialize<string[]>(employee.Roles)!.Contains("driver", StringComparer.OrdinalIgnoreCase)) return [];
        var readiness = await new EfReadinessStore(db).ForDriversAsync([subjectId], today, cancellationToken);
        if (!readiness.TryGetValue(subjectId, out var result)) return [];
        return result.Causes.Where(x => x.Code is ReadinessCauseCode.DocumentExpired or ReadinessCauseCode.DocumentMissing && x.DocumentType is not null).Select(x => new AlertCondition(x.DocumentType!, AlertSeverity.Critical, null)).ToArray();
    }
}

public sealed class A03DocumentEvaluator : IAlertEvaluator
{
    public string RuleId => "A-03";
    public string SubjectType => "document";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context.Command);
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == subjectId, cancellationToken);
        if (document is null || document.Status != DocumentStatus.Uploaded) return [];
        var queueDays = await context.Settings.GetIntAsync(new SettingKey("alerts.a03.queue_days"), context.Command, cancellationToken);
        var blockedHours = await context.Settings.GetIntAsync(new SettingKey("alerts.a03.blocked_hours"), context.Command, cancellationToken);
        var threshold = document.CreatedAt.AddDays(queueDays);
        var blocked = DocumentsCatalog.Get(document.Type).Blocking;
        var holds = now >= threshold || blocked && now >= document.CreatedAt.AddHours(blockedHours);
        return holds ? [new AlertCondition(document.Type.ToString().ToSnakeCase(), AlertSeverity.Warning, null)] : [];
    }
}

public sealed class A05VehicleEvaluator : IAlertEvaluator
{
    public string RuleId => "A-05";
    public string SubjectType => "vehicle";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context.Command);
        var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == subjectId, cancellationToken);
        if (vehicle is null || vehicle.Status != VehicleStatus.Active) return [];
        var readiness = await new EfReadinessStore(db).ForVehiclesAsync([subjectId], today, cancellationToken);
        if (!readiness.TryGetValue(subjectId, out var result)) return [];
        return result.Causes.Where(x => x.Code == ReadinessCauseCode.DocumentExpired && x.DocumentType is not null).Select(x => new AlertCondition(x.DocumentType!, AlertSeverity.Critical, today)).ToArray();
    }

}

internal static class AlertEvaluatorDb
{
    public static FleetOpsDbContext CreateDb(CommandContext context)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options);
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
}

public sealed class AlertPostCommandEvaluation(IAlertEngine engine, ILogger<AlertPostCommandEvaluation> logger) : IPostCommandEvaluation
{
    public async Task EvaluateAsync(IReadOnlyCollection<(string SubjectType, Guid Id)> subjects, CommandContext context, CancellationToken cancellationToken)
    {
        var expanded = subjects.ToHashSet();
        await using (var savepoint = new NpgsqlCommand("savepoint alert_fanout", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction))
        {
            await savepoint.ExecuteNonQueryAsync(cancellationToken);
            try
            {
                await using var db = AlertEvaluatorDb.CreateDb(context);
                foreach (var subject in subjects)
                {
                    if (subject.SubjectType == "document")
                    {
                        var owner = await db.Documents.AsNoTracking().Where(document => document.Id == subject.Id).Select(document => new { document.OwnerType, document.OwnerId }).SingleOrDefaultAsync(cancellationToken);
                        if (owner is not null) expanded.Add((owner.OwnerType == DocumentOwnerType.Vehicle ? "vehicle" : "employee", owner.OwnerId));
                    }
                }
                var vehicleIds = expanded.Where(subject => subject.SubjectType == "vehicle").Select(subject => subject.Id).ToHashSet();
                var cycleIds = expanded.Where(subject => subject.SubjectType == "cycle").Select(subject => subject.Id).ToArray();
                var cycleVehicles = await db.Cycles.AsNoTracking().Where(cycle => cycleIds.Contains(cycle.Id)).Select(cycle => cycle.VehicleId).ToListAsync(cancellationToken);
                vehicleIds.UnionWith(cycleVehicles);
                var affectedCycles = await db.Cycles.AsNoTracking().Where(cycle => vehicleIds.Contains(cycle.VehicleId)).Select(cycle => cycle.Id).ToListAsync(cancellationToken);
                foreach (var cycleId in affectedCycles) expanded.Add(("cycle", cycleId));
            }
            catch (Exception exception)
            {
                await using var rollback = new NpgsqlCommand("rollback to savepoint alert_fanout", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
                await rollback.ExecuteNonQueryAsync(CancellationToken.None);
                logger.LogError("Alert fanout failed. ExceptionType={ExceptionType}", exception.GetType().Name);
                expanded = subjects.ToHashSet();
            }
        }
        foreach (var subject in expanded)
        {
            await using var savepoint = new NpgsqlCommand("savepoint alerts", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
            await savepoint.ExecuteNonQueryAsync(cancellationToken);
            try { await engine.ApplyAsync(subject.SubjectType, subject.Id, context, cancellationToken); }
            catch (Exception exception)
            {
                await using var rollback = new NpgsqlCommand("rollback to savepoint alerts", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
                await rollback.ExecuteNonQueryAsync(CancellationToken.None);
                logger.LogError("Alert evaluation failed. ExceptionType={ExceptionType} RuleId={RuleId}", exception.GetType().Name, "unknown");
            }
        }
    }
}
