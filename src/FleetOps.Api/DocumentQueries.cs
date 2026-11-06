using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using FleetOps.Api.Pipeline;
using FleetOps.Migrations;
using FleetOps.Modules.Documents;
using FleetOps.People;
using FleetOps.Kernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

public sealed record DocumentListCursor(DateTimeOffset UploadedAt, Guid Id);
public sealed class DocumentInvalidCursorException : Exception { public DocumentInvalidCursorException() : base("invalid_cursor") { } }
public sealed class DocumentListQueryHandler(IConfiguration configuration, IDataProtectionProvider protectionProvider) : IQueryHandler<ListDocumentsQuery, DocumentListResult>
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("FleetOps.DocumentListCursor.v1");
    public async Task<DocumentListResult> HandleAsync(ListDocumentsQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var cursor = Unprotect(query.Cursor);
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.")).Options;
        await using var db = new FleetOpsDbContext(options);
        var rows = db.Documents.AsNoTracking().AsQueryable();
        if (query.OwnerType.HasValue) rows = rows.Where(x => x.OwnerType == query.OwnerType.Value);
        if (query.OwnerId.HasValue) rows = rows.Where(x => x.OwnerId == query.OwnerId.Value);
        if (query.Status.HasValue) rows = rows.Where(x => x.Status == query.Status.Value);
        if (actor.Roles.Contains(RoleCode.Dispatcher)) rows = rows.Where(x => x.OwnerType == DocumentOwnerType.Vehicle);
        if (!string.IsNullOrWhiteSpace(query.Search)) { var s = query.Search.Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal); rows = rows.Where(x => EF.Functions.ILike(x.Number ?? "", $"%{s}%", "\\") || (x.OwnerType == DocumentOwnerType.Vehicle && db.Vehicles.Where(v => v.Id == x.OwnerId).Select(v => v.Plate).Any(p => EF.Functions.ILike(p, $"%{s}%", "\\"))) || (x.OwnerType == DocumentOwnerType.Employee && db.Set<Employee>().Where(e => e.Id == x.OwnerId).Any(e => EF.Functions.ILike(e.FirstName + " " + e.LastName, $"%{s}%", "\\")))); }
        if (cursor is not null) rows = rows.Where(x => x.CreatedAt < cursor.UploadedAt || x.CreatedAt == cursor.UploadedAt && x.Id.CompareTo(cursor.Id) < 0);
        var entities = await rows.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(query.Limit + 1).ToListAsync(cancellationToken);
        var hasNext = entities.Count > query.Limit;
        if (hasNext) entities.RemoveAt(entities.Count - 1);
        var employeeIds = entities.Where(x => x.OwnerType == DocumentOwnerType.Employee).Select(x => x.OwnerId).ToArray();
        var vehicleIds = entities.Where(x => x.OwnerType == DocumentOwnerType.Vehicle).Select(x => x.OwnerId).ToArray();
        var names = await db.Set<Employee>().Where(x => employeeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName, cancellationToken);
        var plates = await db.Vehicles.Where(x => vehicleIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Plate, cancellationToken);
        var result = entities.Select(x => new DocumentListItem(x.Id, x.OwnerType, x.OwnerId, x.OwnerType == DocumentOwnerType.Employee ? names.GetValueOrDefault(x.OwnerId, "") : plates.GetValueOrDefault(x.OwnerId, ""), x.Type, x.Number, x.IssuedAt, x.ExpiresAt, x.Status, x.CreatedAt, x.Version)).ToList();
        return new(result, hasNext ? protector.Protect(JsonSerializer.Serialize(new DocumentListCursor(entities[^1].CreatedAt, entities[^1].Id))) : null);
    }
    private DocumentListCursor? Unprotect(string? value) { if (string.IsNullOrWhiteSpace(value)) return null; try { var c = JsonSerializer.Deserialize<DocumentListCursor>(protector.Unprotect(value)); return c is null || c.Id == Guid.Empty ? throw new DocumentInvalidCursorException() : c; } catch (DocumentInvalidCursorException) { throw; } catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException) { throw new DocumentInvalidCursorException(); } }
}
public sealed class DocumentDetailQueryHandler(IConfiguration configuration) : IQueryHandler<GetDocumentQuery, DocumentDetail>
{
    public async Task<DocumentDetail> HandleAsync(GetDocumentQuery query, AccessContext actor, CancellationToken cancellationToken) { var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.")).Options; await using var db = new FleetOpsDbContext(options); var row = await EfDocumentStore.ReadDetailAsync(db, query.SubjectId ?? throw new DocumentValidationException(["id"]), cancellationToken) ?? throw new DocumentConflictException("not_found"); if (actor.Roles.Contains(RoleCode.Dispatcher) && row.OwnerType == DocumentOwnerType.Employee) throw new DocumentConflictException("not_found"); return row; }
}
public sealed class PeopleLookupQueryHandler(IConfiguration configuration) : IQueryHandler<LookupPeopleQuery, PeopleLookupResult>
{
    public async Task<PeopleLookupResult> HandleAsync(LookupPeopleQuery query, AccessContext actor, CancellationToken cancellationToken) { var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.")).Options; await using var db = new FleetOpsDbContext(options); var rows = db.Set<Employee>().AsNoTracking().Where(x => x.Status != EmployeeStatus.Left); if (!string.IsNullOrWhiteSpace(query.Search)) { var s = query.Search.Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal); rows = rows.Where(x => EF.Functions.ILike(x.FirstName + " " + x.LastName, $"%{s}%", "\\")); } return new(await rows.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).Take(20).Select(x => new PeopleLookupItem(x.Id, x.FirstName + " " + x.LastName)).ToListAsync(cancellationToken)); }
}
