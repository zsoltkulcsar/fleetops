using System.Text.Json;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Notifications;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace FleetOps.Migrations;
public sealed class NotificationQueueStore(IConfiguration configuration, TimeProvider time, ICompanyTimeService companyTime, INotificationIds ids) : INotificationQueueStore
{
    public async Task<IReadOnlyList<NotificationRecipient>> RecipientsAsync(Guid employeeId, bool includeCover, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration, context);
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        var recipients = new List<Guid> { employeeId };
        if (includeCover)
            recipients.AddRange(await db.AlertCovers.Where(cover => cover.FromId == employeeId && cover.From <= today && cover.To >= today).Select(cover => cover.ToId).ToListAsync(cancellationToken));
        var employees = await db.Employees.AsNoTracking().Where(employee => recipients.Contains(employee.Id) && employee.Status == EmployeeStatus.Active && (employee.EndDate == null || employee.EndDate >= today)).ToListAsync(cancellationToken);
        var users = await db.Users.AsNoTracking().Where(user => recipients.Contains(user.EmployeeId)).ToListAsync(cancellationToken);
        var subscribed = await db.PushSubscriptions.Where(subscription => recipients.Contains(subscription.EmployeeId)).Select(subscription => subscription.EmployeeId).Distinct().ToListAsync(cancellationToken);
        var result = employees.Select(employee =>
        {
            var user = users.SingleOrDefault(user => user.EmployeeId == employee.Id);
            var roles = JsonSerializer.Deserialize<string[]>(employee.Roles) ?? [];
            var office = user is not null && roles.Any(role => role is "hr" or "dispatcher" or "owner");
            var preferences = user is null ? new NotifyPreferences(false, false, false) : new(user.CriticalPush, user.CriticalEmail, user.DigestEmail);
            return new NotificationRecipient(employee.Id, preferences, office, subscribed.Contains(employee.Id));
        }).Where(recipient => recipient.EmployeeId == employeeId || recipient.Office).ToList();
        // Direct in-app recipients can be uploaders without a linked employee record.
        if (!await db.Employees.AnyAsync(employee => employee.Id == employeeId, cancellationToken))
            result.Add(new NotificationRecipient(employeeId, new NotifyPreferences(false, false, false), false, false));
        return result;
    }
    public async Task<Guid> InsertAsync(Guid employeeId, string template, object parameters, Guid? alertId, NotificationChannel channel, CommandContext context, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand("insert into fleetops.notifications (id, employee_id, alert_id, template_code, parameters, channel, created_at, attempts, next_attempt_at) values ($1,$2,$3,$4,$5::jsonb,$6,$7,0,$7) on conflict do nothing returning id", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(employeeId);
        command.Parameters.AddWithValue(alertId is null ? DBNull.Value : alertId.Value);
        command.Parameters.AddWithValue(template);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(parameters));
        command.Parameters.AddWithValue(channel.ToString().ToSnakeCase());
        command.Parameters.AddWithValue(time.GetUtcNow());
        if (await command.ExecuteScalarAsync(cancellationToken) is not Guid inserted) return Guid.Empty;
        ids.Add(inserted);
        return inserted;
    }
}
