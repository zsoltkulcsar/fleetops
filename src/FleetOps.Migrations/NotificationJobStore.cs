using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Notifications;
using FleetOps.Modules.Alerts;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;
namespace FleetOps.Migrations;
public sealed class NotificationJobStore(IConfiguration configuration, TimeProvider time, INotificationIds ids, ICompanyTimeService companyTime) : INotificationJobStore
{
    public async Task<IReadOnlyList<EscalationCandidate>> LockEscalationsAsync(DateTimeOffset cutoff, string? cursor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select id, rule_id, assigned_role from fleetops.alerts alert where severity = 'Critical' and status = 'Open' and escalated_at is null and opened_at <= $1 and ($2::uuid is null or id > $2) and (rule_id <> 'A-07' or exists (select 1 from fleetops.defect_reports defect where defect.id = alert.subject_id and defect.status = 'open')) order by id limit 100 for update", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
        command.Parameters.AddWithValue(cutoff);
        command.Parameters.AddWithValue(cursor is null ? DBNull.Value : Guid.Parse(cursor));
        var alerts = new List<EscalationCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) alerts.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return alerts;
    }
    public async Task<IReadOnlyList<Guid>> OfficeRecipientsAsync(string[] roles, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration, context);
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        var employees = await db.Employees.AsNoTracking().Where(employee => employee.Status == EmployeeStatus.Active && (employee.EndDate == null || employee.EndDate >= today) && db.Users.Any(user => user.EmployeeId == employee.Id)).ToListAsync(cancellationToken);
        return employees.Where(employee => (JsonSerializer.Deserialize<string[]>(employee.Roles) ?? []).Intersect(roles).Any()).Select(employee => employee.Id).ToArray();
    }
    public async Task MarkEscalatedAsync(Guid id, DateTimeOffset now, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration, context);
        await db.Alerts.Where(alert => alert.Id == id && alert.EscalatedAt == null).ExecuteUpdateAsync(update => update.SetProperty(alert => alert.EscalatedAt, now).SetProperty(alert => alert.Version, alert => alert.Version + 1), cancellationToken);
    }
    public async Task<IReadOnlyList<Guid>> DigestUsersAsync(string? cursor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration, context);
        var after = cursor is null ? Guid.Empty : Guid.Parse(cursor);
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        return await db.Employees.Where(employee => employee.Status == EmployeeStatus.Active && (employee.EndDate == null || employee.EndDate >= today) && employee.Id.CompareTo(after) > 0 &&
            (employee.Roles.Contains("\"hr\"") || employee.Roles.Contains("\"dispatcher\"") || employee.Roles.Contains("\"owner\"")) &&
            db.Users.Any(user => user.EmployeeId == employee.Id && user.DigestEmail)).OrderBy(employee => employee.Id).Take(100).Select(employee => employee.Id).ToListAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<DigestAlert>> DigestAlertsAsync(Guid employeeId, DateOnly today, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration, context);
        var coveredIds = await db.AlertCovers.Where(cover => cover.ToId == employeeId && cover.From <= today && cover.To >= today).Select(cover => cover.FromId).ToListAsync(cancellationToken);
        coveredIds.Add(employeeId);
        var roleJson = await db.Employees.Where(employee => coveredIds.Contains(employee.Id) && employee.Status == EmployeeStatus.Active && (employee.EndDate == null || employee.EndDate >= today)).Select(employee => employee.Roles).ToListAsync(cancellationToken);
        var roles = roleJson.SelectMany(json => JsonSerializer.Deserialize<string[]>(json) ?? []).Distinct().ToArray();
        var owner = roles.Contains("owner");
        return await db.Alerts.AsNoTracking().Where(alert => alert.Status == AlertStatus.Open && (owner || roles.Contains(alert.AssignedRole) || (alert.AssignedRole == "hr,owner" && roles.Contains("hr"))))
            .OrderByDescending(alert => alert.Severity).ThenBy(alert => alert.OpenedAt).Select(alert => new DigestAlert(alert.Id, alert.RuleId)).ToListAsync(cancellationToken);
    }
    public async Task QueueDigestAsync(Guid employeeId, DateOnly today, object parameters, CommandContext context, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("insert into fleetops.notifications (id, employee_id, template_code, parameters, channel, created_at, attempts, next_attempt_at, digest_date) values ($1,$2,'daily_digest',$3::jsonb,'email',$4,0,$4,$5) on conflict do nothing returning id", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
        command.Parameters.AddWithValue(Guid.CreateVersion7());
        command.Parameters.AddWithValue(employeeId);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(parameters));
        command.Parameters.AddWithValue(time.GetUtcNow());
        command.Parameters.AddWithValue(today);
        if (await command.ExecuteScalarAsync(cancellationToken) is Guid id) ids.Add(id);
    }
}
