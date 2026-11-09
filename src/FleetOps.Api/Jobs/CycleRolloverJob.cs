using FleetOps.Api.Pipeline;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Schedule;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetOps.Api.Jobs;

public sealed class CycleRolloverJob(IConfiguration configuration, ICycleStore store, ICompanyTimeService companyTime) : IJob
{
    // A stable non-personal ID for the system actor required by the audit interceptor.
    public static readonly Guid ActorId = Guid.Parse("00000000-0000-0000-0000-000000000032");
    public string Name => "cycles-rollover";

    public async Task<JobStep> RunStepAsync(JobContext context, CancellationToken cancellationToken)
    {
        var connectionString = DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(connection).Options;
        var cursor = context.Cursor is null ? Guid.Empty : Guid.Parse(context.Cursor);
        await using var db = new FleetOpsDbContext(options);
        var employees = await db.Employees.AsNoTracking().Where(employee => employee.Id.CompareTo(cursor) > 0 && employee.Status == EmployeeStatus.Active && employee.CyclePattern != null)
            .OrderBy(employee => employee.Id).Take(50).Select(employee => employee.Id).ToListAsync(cancellationToken);
        var today = companyTime.ToCompanyDate(context.Now);
        foreach (var employeeId in employees)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var commandContext = new CommandContext(connection, transaction);
            await EfCycleStore.LockScheduleAsync(commandContext, cancellationToken);
            await EfCycleStore.LockEmployeeAsync(employeeId, commandContext, cancellationToken);
            db.Database.UseTransaction(transaction);
            // Read after locking so a concurrent pattern save cannot feed this job an old pattern.
            var employee = await db.Employees.AsNoTracking().SingleAsync(employee => employee.Id == employeeId, cancellationToken);
            if (employee.CyclePattern is not null && employee.Status == EmployeeStatus.Active && (System.Text.Json.JsonSerializer.Deserialize<string[]>(employee.Roles)?.Contains("driver") ?? false) && (employee.EndDate is null || employee.EndDate >= today)
                && await db.Vehicles.AnyAsync(vehicle => vehicle.Id == employee.CyclePattern.VehicleId && vehicle.Status == FleetOps.Modules.Fleet.VehicleStatus.Active, cancellationToken))
            {
                var existing = await store.LoadCyclesAsync(employeeId, commandContext, cancellationToken);
                var missing = CycleGeneration.Missing(employeeId, employee.CyclePattern, today, existing);
                await store.AddCyclesAsync(missing, commandContext, ActorId, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            db.Database.UseTransaction((System.Data.Common.DbTransaction?)null);
        }
        return new(employees.Count < 50, employees.Count == 0 ? context.Cursor : employees[^1].ToString(), employees.Count);
    }
}
