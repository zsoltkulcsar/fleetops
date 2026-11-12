using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Notifications;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace FleetOps.Migrations;
public sealed class AlertCoverStore(IConfiguration configuration) : IAlertCoverStore
{
    public async Task<AlertCoverList> ListAsync(Guid employeeId, CommandContext? context, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration, context);
        return new(await db.AlertCovers.AsNoTracking().Where(cover => cover.FromId == employeeId).OrderBy(cover => cover.From)
            .Join(db.Employees, cover => cover.ToId, employee => employee.Id, (cover, employee) => new AlertCoverItem(cover.Id, cover.ToId, employee.FirstName + " " + employee.LastName, cover.From, cover.To)).ToListAsync(cancellationToken));
    }
    public async Task<bool> SaveAsync(Guid employeeId, SaveAlertCoverCommand command, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration, context);
        // Serialize a person's edits, including overlapping new covers.
        await using var personLock = new NpgsqlCommand("select id from fleetops.employees where id = $1 for update", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
        personLock.Parameters.AddWithValue(employeeId);
        await personLock.ExecuteScalarAsync(cancellationToken);
        var eligible = await db.Employees.AnyAsync(employee => employee.Id == command.ToId && employee.Status == EmployeeStatus.Active &&
            (employee.EndDate == null || employee.EndDate >= command.To) && (employee.Roles.Contains("\"hr\"") || employee.Roles.Contains("\"dispatcher\"") || employee.Roles.Contains("\"owner\"")), cancellationToken);
        if (!eligible || !await db.Users.AnyAsync(user => user.EmployeeId == command.ToId, cancellationToken)) return false;
        if (await db.AlertCovers.AnyAsync(cover => cover.FromId == employeeId && cover.Id != command.Id && cover.From <= command.To && cover.To >= command.From, cancellationToken)) return false;
        if (command.Id is Guid id)
            return await db.AlertCovers.Where(cover => cover.Id == id && cover.FromId == employeeId).ExecuteUpdateAsync(update => update
                .SetProperty(cover => cover.ToId, command.ToId).SetProperty(cover => cover.From, command.From).SetProperty(cover => cover.To, command.To), cancellationToken) == 1;
        db.AlertCovers.Add(new() { FromId = employeeId, ToId = command.ToId, From = command.From, To = command.To });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
    public async Task<bool> DeleteAsync(Guid employeeId, Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration, context);
        return await db.AlertCovers.Where(cover => cover.Id == id && cover.FromId == employeeId).ExecuteDeleteAsync(cancellationToken) == 1;
    }
}
