using System.Text.Json;
using FleetOps.Identity;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Audit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfPersonAccessStore(IConfiguration configuration, TimeProvider time, IOfficeInvitationMail mail) : IPersonAccessStore
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
    public async Task<PersonSessions> SessionsAsync(Guid personId, CancellationToken cancellationToken)
    {
        await using var db = Open(null);
        var sessions = await db.OfficeSessions.AsNoTracking().Where(session => session.EmployeeId == personId).OrderByDescending(session => session.CreatedAt)
            .Select(session => new PersonSession(session.Id, session.Device, session.CreatedAt, session.LastSeenAt,
                session.RevokedAt == null ? "active" : "revoked", session.RevokeReason, session.PinResetRequestedAt)).ToArrayAsync(cancellationToken);
        return new(sessions);
    }
    public async Task<AccessPerson?> LockPersonAsync(Guid personId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(700077)", cancellationToken);
        var person = await db.Employees.FromSqlInterpolated($"SELECT * FROM fleetops.employees WHERE id = {personId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return person is null ? null : new(person.Id, person.Phone, person.Email, JsonSerializer.Deserialize<string[]>(person.Roles) ?? [], person.PersonnelNumber);
    }
    public async Task<int> OwnersAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var roles = await db.Employees.AsNoTracking().Select(person => person.Roles).ToArrayAsync(cancellationToken);
        return roles.Count(value => JsonSerializer.Deserialize<string[]>(value)?.Contains("owner") == true);
    }
    public async Task RevokeAsync(Guid personId, Guid sessionId, string reason, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var session = await db.OfficeSessions.SingleOrDefaultAsync(session => session.EmployeeId == personId && session.Id == sessionId, cancellationToken) ?? throw new AccessConflictException("not_found");
        if (session.RevokedAt is not null) return;
        session.RevokedAt = time.GetUtcNow();
        session.RevokeReason = reason;
        Audit(db, "session", sessionId, "revokedAt", null, session.RevokedAt.Value.ToString("O"), reason, actor);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task ResetPinAsync(Guid personId, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var sessions = await db.OfficeSessions.Where(session => session.EmployeeId == personId && session.Device == "driver" && session.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            var old = session.PinResetRequestedAt;
            session.PinResetRequestedAt = time.GetUtcNow();
            Audit(db, "session", session.Id, "pinResetRequestedAt", old?.ToString("O"), session.PinResetRequestedAt.Value.ToString("O"), "pin_reset_requested", actor);
        }
        Audit(db, "employee", personId, "pinReset", null, "requested", "pin_reset_requested", actor);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task CompletePinResetAsync(Guid personId, Guid sessionId, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var session = await db.OfficeSessions.SingleOrDefaultAsync(session => session.Id == sessionId && session.EmployeeId == personId && session.Device == "driver" && session.RevokedAt == null, cancellationToken)
            ?? throw new AuthorizationException("session_revoked", "driver.pin_reset.complete", []);
        var old = session.PinResetRequestedAt;
        session.PinResetRequestedAt = null;
        Audit(db, "session", sessionId, "pinResetRequestedAt", old?.ToString("O"), null, "pin_reset_completed", actor);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<bool> PinResetRequiredAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var db = Open(null);
        return await db.OfficeSessions.AnyAsync(session => session.Id == sessionId && session.RevokedAt == null && session.PinResetRequestedAt != null, cancellationToken);
    }
    public async Task ChangePhoneAsync(Guid personId, string phone, string reason, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var person = await db.Employees.SingleAsync(person => person.Id == personId, cancellationToken);
        var old = person.Phone;
        person.Phone = phone;
        foreach (var session in await db.OfficeSessions.Where(session => session.EmployeeId == personId && session.Device == "driver" && session.RevokedAt == null).ToListAsync(cancellationToken))
        {
            session.RevokedAt = time.GetUtcNow();
            session.RevokeReason = "number_changed";
            Audit(db, "session", session.Id, "revokedAt", null, session.RevokedAt.Value.ToString("O"), "number_changed", actor);
        }
        await db.EnrolmentCodes.Where(code => code.EmployeeId == personId && code.UsedAt == null).ExecuteUpdateAsync(setters => setters.SetProperty(code => code.UsedAt, time.GetUtcNow()), cancellationToken);
        Audit(db, "employee", personId, "phone", old, phone, reason, actor);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task SetRolesAsync(Guid personId, string[] roles, bool revokeOfficeSessions, string reason, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var person = await db.Employees.SingleAsync(person => person.Id == personId, cancellationToken);
        var old = person.Roles;
        person.Roles = JsonSerializer.Serialize(roles);
        if (revokeOfficeSessions)
        {
            foreach (var session in await db.OfficeSessions.Where(session => session.EmployeeId == personId && session.Device != "driver" && session.RevokedAt == null).ToListAsync(cancellationToken))
            {
                session.RevokedAt = time.GetUtcNow();
                session.RevokeReason = "office_roles_removed";
                Audit(db, "session", session.Id, "revokedAt", null, session.RevokedAt.Value.ToString("O"), "office_roles_removed", actor);
            }
        }
        Audit(db, "employee", personId, "roles", old, person.Roles, reason, actor);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task InviteAsync(AccessPerson person, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var user = await db.Users.SingleOrDefaultAsync(user => user.EmployeeId == person.Id, cancellationToken);
        if (user?.PasswordHash is not null) throw new AccessConflictException("office_login_exists");
        if (await db.Users.AnyAsync(other => other.EmployeeId != person.Id && other.NormalizedEmail == person.Email.ToUpperInvariant(), cancellationToken)) throw new AccessValidationException(["email"]);
        if (user is null)
        {
            user = new OfficeUser { Id = Guid.NewGuid(), EmployeeId = person.Id, Email = person.Email, UserName = person.Email, NormalizedEmail = person.Email.ToUpperInvariant(), NormalizedUserName = person.Email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString(), LockoutEnabled = true };
            using var users = new UserStore<OfficeUser, IdentityRole<Guid>, FleetOpsDbContext, Guid>(db);
            if (!(await users.CreateAsync(user, cancellationToken)).Succeeded) throw new AccessValidationException(["email"]);
        }
        else
        {
            user.Email = person.Email;
            user.NormalizedEmail = person.Email.ToUpperInvariant();
        }
        var now = time.GetUtcNow();
        foreach (var prior in await db.PasswordResetTokens.Where(token => token.UserId == user.Id && token.UsedAt == null).ToListAsync(cancellationToken)) prior.UsedAt = now;
        var raw = SessionTokens.New();
        db.PasswordResetTokens.Add(new PasswordResetToken { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = SessionTokens.Hash(raw), ExpiresAt = now.Add(mail.TokenLifetime) });
        Audit(db, "employee", person.Id, "officeInvite", null, "sent", "office_invited", actor);
        await db.SaveChangesAsync(cancellationToken);
        mail.Send(person.Email, raw);
    }
    private void Audit(FleetOpsDbContext db, string entityType, Guid id, string field, string? old, string? value, string reason, AccessContext actor) => db.AuditEntries.Add(new AuditEntry
    {
        Id = Guid.NewGuid(), EntityType = entityType, EntityId = id, FieldName = field, OldValue = old, NewValue = value,
        ActorId = actor.EmployeeId.ToString(), Reason = reason, CreatedAt = time.GetUtcNow()
    });
}
