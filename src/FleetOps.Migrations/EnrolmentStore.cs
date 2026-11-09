using FleetOps.Identity;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Audit;
using FleetOps.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfEnrolmentStore(IConfiguration configuration) : IEnrolmentStore
{
    private FleetOpsDbContext CreateDb(CommandContext? context)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        if (context is null)
            MigrationDbContextOptions.Configure(options, configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured."));
        else
            MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options);
        if (context is not null)
            db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
    private static EnrolmentEmployee Project(Employee employee) => new(employee.Id, employee.PersonnelNumber, employee.Phone, employee.Status == EmployeeStatus.Active, employee.EndDate, employee.Roles, employee.FirstName);
    public async Task<EnrolmentEmployee?> LockEmployeeAsync(Guid id, CommandContext context, CancellationToken ct)
    {
        await using var db = CreateDb(context);
        var employee = await db.Employees.FromSqlInterpolated($"SELECT * FROM fleetops.employees WHERE id = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        return employee is null ? null : Project(employee);
    }
    public async Task<EnrolmentEmployee?> FindEmployeeAsync(Guid id, CommandContext? context, CancellationToken ct)
    {
        await using var db = CreateDb(context);
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(employee => employee.Id == id, ct);
        return employee is null ? null : Project(employee);
    }
    public async Task<IReadOnlyList<EnrolmentCode>> ActiveCodesAsync(DateTimeOffset now, int maxTries, CommandContext context, CancellationToken ct)
    {
        await using var db = CreateDb(context);
        return await db.EnrolmentCodes.AsNoTracking().Where(code => code.UsedAt == null && code.ExpiresAt > now && code.Attempts < maxTries).OrderBy(code => code.EmployeeId).ThenBy(code => code.Id).ToListAsync(ct);
    }
    public async Task ReplaceCodeAsync(EnrolmentCode code, CommandContext context, CancellationToken ct)
    {
        await using var db = CreateDb(context);
        await db.OfficeSessions.Where(session => session.EmployeeId == code.EmployeeId && session.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAt, code.CreatedAt), ct);
        await db.EnrolmentCodes.Where(older => older.EmployeeId == code.EmployeeId && older.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(older => older.UsedAt, code.CreatedAt), ct);
        db.EnrolmentCodes.Add(code);
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            EntityType = "employee",
            EntityId = code.EmployeeId,
            FieldName = "enrolment",
            NewValue = "sent",
            ActorId = code.IssuedBy.ToString(),
            Reason = "enrolment_sent",
            CreatedAt = code.CreatedAt
        });
        await db.SaveChangesAsync(ct);
    }
    public async Task IncrementAttemptsAsync(Guid id, DateTimeOffset now, int maxTries, CommandContext context, CancellationToken ct)
    {
        await using var db = CreateDb(context);
        await db.EnrolmentCodes.Where(code => code.Id == id && code.UsedAt == null && code.ExpiresAt > now && code.Attempts < maxTries)
            .ExecuteUpdateAsync(setters => setters.SetProperty(code => code.Attempts, code => code.Attempts + 1), ct);
    }
    public async Task<bool> ConsumeAsync(Guid id, DateTimeOffset now, int maxTries, CommandContext context, CancellationToken ct)
    {
        await using var db = CreateDb(context);
        return await db.EnrolmentCodes.Where(code => code.Id == id && code.UsedAt == null && code.ExpiresAt > now && code.Attempts < maxTries)
            .ExecuteUpdateAsync(setters => setters.SetProperty(code => code.UsedAt, now), ct) == 1;
    }
    public async Task<Guid?> FindPasswordlessUserAsync(Guid employeeId, CommandContext context, CancellationToken ct)
    {
        await using var db = CreateDb(context);
        return await db.Users.Where(user => user.EmployeeId == employeeId && user.PasswordHash == null).Select(user => (Guid?)user.Id).SingleOrDefaultAsync(ct);
    }
    public async Task AddUserAsync(OfficeUser user, CommandContext context, CancellationToken ct)
    {
        await using var db = CreateDb(context);
        using var store = new UserStore<OfficeUser, IdentityRole<Guid>, FleetOpsDbContext, Guid>(db);
        var result = await store.CreateAsync(user, ct);
        if (!result.Succeeded)
            throw new InvalidOperationException("Driver account creation failed.");
    }
    public async Task<bool> HasPrivacyAsync(Guid employeeId, CommandContext context, CancellationToken ct)
    {
        await using var db = CreateDb(context);
        return await db.PrivacyAcknowledgements.AnyAsync(acknowledgement => acknowledgement.EmployeeId == employeeId && acknowledgement.Version == SessionTokens.PrivacyVersion, ct);
    }
    public async Task AddSessionAsync(OfficeSession session, CommandContext context, CancellationToken ct)
    {
        await using var db = CreateDb(context);
        db.OfficeSessions.Add(session);
        await db.SaveChangesAsync(ct);
    }
}
