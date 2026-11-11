using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Audit;
using FleetOps.Modules.Documents;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetOps.Migrations;

public sealed class EfPeopleStore(IConfiguration configuration, TimeProvider time, IChangedSubjects? changedSubjects = null) : IPeopleStore
{
    private FleetOpsDbContext Open(CommandContext? context)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        if (context is null) MigrationDbContextOptions.Configure(options, configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured."));
        else MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options);
        if (context is not null) db.Database.UseTransaction(context.DbTransaction);
        return db;
    }

    public async Task<Employee?> LockAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        return await db.Employees.FromSqlInterpolated($"SELECT * FROM fleetops.employees WHERE id = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid> DepotAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var depots = await db.Depots.Select(depot => depot.Id).Take(2).ToArrayAsync(cancellationToken);
        if (depots.Length != 1) throw new PersonConflictException("depot_not_configured");
        return depots[0];
    }

    public async Task<bool> PersonnelNumberExistsAsync(string number, Guid exceptId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        return await db.Employees.AnyAsync(employee => employee.Id != exceptId && employee.PersonnelNumber == number, cancellationToken);
    }

    public async Task SaveAsync(Employee employee, bool create, IReadOnlyList<PersonAudit> audit, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        if (create) db.Employees.Add(employee);
        else db.Employees.Update(employee);
        foreach (var change in audit)
        {
            db.AuditEntries.Add(new AuditEntry
            {
                Id = Guid.NewGuid(),
                EntityType = "employee",
                EntityId = employee.Id,
                FieldName = change.Field,
                OldValue = change.OldValue,
                NewValue = change.NewValue,
                ActorId = actor.EmployeeId.ToString(),
                Reason = change.Reason ?? (create ? "person_created" : "person_updated"),
                CreatedAt = time.GetUtcNow()
            });
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            changedSubjects?.Add("employee", employee.Id);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_employees_personnel_number" })
        {
            throw new PersonValidationException(["personnelNumber"]);
        }
    }

    public async Task<IReadOnlyList<PersonFacts>> ReadAsync(DateOnly today, Guid? id, CommandContext? context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var employees = await db.Employees.AsNoTracking().Where(employee => id == null || employee.Id == id).ToArrayAsync(cancellationToken);
        var ids = employees.Select(employee => employee.Id).ToArray();
        var documents = await db.Documents.AsNoTracking().Where(document => document.OwnerType == DocumentOwnerType.Employee && ids.Contains(document.OwnerId)).ToArrayAsync(cancellationToken);
        var readiness = await new EfReadinessStore(db).ForDriversAsync(ids, today, cancellationToken);
        var assignments = await db.Assignments.AsNoTracking().Where(assignment => ids.Contains(assignment.EmployeeId) && assignment.From <= today && assignment.To == null)
            .OrderByDescending(assignment => assignment.From).ThenBy(assignment => assignment.Id).ToArrayAsync(cancellationToken);
        var vehicleIds = assignments.Select(assignment => assignment.VehicleId).Concat(employees.Where(employee => employee.CyclePattern != null).Select(employee => employee.CyclePattern!.VehicleId)).Distinct().ToArray();
        var plates = await db.Vehicles.AsNoTracking().Where(vehicle => vehicleIds.Contains(vehicle.Id)).ToDictionaryAsync(vehicle => vehicle.Id, vehicle => vehicle.Plate, cancellationToken);
        return employees.Select(employee => new PersonFacts(employee,
            documents.Where(document => document.OwnerId == employee.Id).Select(document => new PersonDocumentFact(TypeName(document.Type), document.Status.ToString().ToLowerInvariant(), document.ExpiresAt, document.CreatedAt, document.Version)).ToArray(),
            readiness[employee.Id].CanDrive,
            readiness[employee.Id].Causes.Select(cause => cause.DocumentType ?? cause.Code.ToString().ToLowerInvariant()).ToArray(),
            plates.GetValueOrDefault(assignments.FirstOrDefault(assignment => assignment.EmployeeId == employee.Id)?.VehicleId ?? employee.CyclePattern?.VehicleId ?? Guid.Empty))).ToArray();
    }

    private static string TypeName(DocumentType type) => type switch
    {
        DocumentType.IdCard => "id_card",
        DocumentType.EmploymentContract => "employment_contract",
        DocumentType.LicenceC_CE => "licence_c_ce",
        DocumentType.DriverCard => "driver_card",
        DocumentType.AdrCertificate => "adr_certificate",
        _ => type.ToString().ToLowerInvariant()
    };
}
