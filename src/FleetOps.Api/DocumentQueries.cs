using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using FleetOps.Api.Pipeline;
using FleetOps.Migrations;
using FleetOps.Modules.Documents;
using FleetOps.People;
using FleetOps.Kernel.Authorization;
using FleetOps.Kernel;
using FleetOps.Kernel.Files;
using Microsoft.EntityFrameworkCore;
using FleetOps.Modules.Setup;
using FleetOps.Modules.Readiness;
using FleetOps.Modules.Fleet;

namespace FleetOps.Api;

public sealed record DocumentListCursor(DateTimeOffset UploadedAt, Guid Id);
public sealed class DocumentInvalidCursorException : Exception { public DocumentInvalidCursorException() : base("invalid_cursor") { } }
public sealed class DocumentListQueryHandler(IConfiguration configuration, IDataProtectionProvider protectionProvider, ICompanyTimeService companyTime, TimeProvider time, ISettings settings) : IQueryHandler<ListDocumentsQuery, DocumentListResult>
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
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        if (string.Equals(query.Filter, "queue", StringComparison.OrdinalIgnoreCase)) rows = rows.Where(x => x.Status == DocumentStatus.Uploaded);
        if (string.Equals(query.Filter, "expiring", StringComparison.OrdinalIgnoreCase)) rows = await ApplyExpiringFilterAsync(rows, today, settings, cancellationToken);
        if (string.Equals(query.Filter, "expired", StringComparison.OrdinalIgnoreCase)) rows = rows.Where(x => x.Status == DocumentStatus.Verified && x.ExpiresAt < today);
        if (actor.Roles.Contains(RoleCode.Dispatcher)) rows = rows.Where(x => x.OwnerType == DocumentOwnerType.Vehicle);
        if (!string.IsNullOrWhiteSpace(query.Search)) { var s = query.Search.Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal); rows = rows.Where(x => EF.Functions.ILike(x.Number ?? "", $"%{s}%", "\\") || (x.OwnerType == DocumentOwnerType.Vehicle && db.Vehicles.Where(v => v.Id == x.OwnerId).Select(v => v.Plate).Any(p => EF.Functions.ILike(p, $"%{s}%", "\\"))) || (x.OwnerType == DocumentOwnerType.Employee && db.Set<Employee>().Where(e => e.Id == x.OwnerId).Any(e => EF.Functions.ILike(e.FirstName + " " + e.LastName, $"%{s}%", "\\")))); }
        var ascending = string.Equals(query.Filter, "queue", StringComparison.OrdinalIgnoreCase);
        if (cursor is not null) rows = ascending ? rows.Where(x => x.CreatedAt > cursor.UploadedAt || x.CreatedAt == cursor.UploadedAt && x.Id.CompareTo(cursor.Id) > 0) : rows.Where(x => x.CreatedAt < cursor.UploadedAt || x.CreatedAt == cursor.UploadedAt && x.Id.CompareTo(cursor.Id) < 0);
        var entities = ascending ? await rows.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(query.Limit + 1).ToListAsync(cancellationToken) : await rows.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(query.Limit + 1).ToListAsync(cancellationToken);
        var hasNext = entities.Count > query.Limit;
        if (hasNext) entities.RemoveAt(entities.Count - 1);
        var employeeIds = entities.Where(x => x.OwnerType == DocumentOwnerType.Employee).Select(x => x.OwnerId).ToArray();
        var vehicleIds = entities.Where(x => x.OwnerType == DocumentOwnerType.Vehicle).Select(x => x.OwnerId).ToArray();
        var names = await db.Set<Employee>().Where(x => employeeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName, cancellationToken);
        var plates = await db.Vehicles.Where(x => vehicleIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Plate, cancellationToken);
        var result = entities.Select(x => new DocumentListItem(x.Id, x.OwnerType, x.OwnerId, x.OwnerType == DocumentOwnerType.Employee ? names.GetValueOrDefault(x.OwnerId, "") : plates.GetValueOrDefault(x.OwnerId, ""), x.Type, x.Number, x.IssuedAt, x.ExpiresAt, x.Status, x.CreatedAt, x.Version, CanAct(x, actor))).ToList();
        return new(result, hasNext ? protector.Protect(JsonSerializer.Serialize(new DocumentListCursor(entities[^1].CreatedAt, entities[^1].Id))) : null);
    }
    private static async Task<IQueryable<Document>> ApplyExpiringFilterAsync(IQueryable<Document> rows, DateOnly today, ISettings settings, CancellationToken cancellationToken)
    {
        var filtered = rows.Where(x => false);
        foreach (var catalog in DocumentsCatalog.All.Where(x => x.HasExpiry))
        {
            var days = await settings.GetIntAsync(new SettingKey($"documents.lead_days.{DocumentTypeKey(catalog.Type)}"), null, cancellationToken);
            filtered = filtered.Concat(rows.Where(x => x.Status == DocumentStatus.Verified && x.Type == catalog.Type && x.ExpiresAt >= today && x.ExpiresAt <= today.AddDays(days)));
        }
        return filtered;
    }
    private static string DocumentTypeKey(DocumentType type) => type switch { DocumentType.LicenceC_CE => "licence_c_ce", DocumentType.DriverCard => "driver_card", DocumentType.IdCard => "id_card", DocumentType.AdrCertificate => "adr_certificate", DocumentType.HuReport => "hu_report", DocumentType.SpReport => "sp_report", DocumentType.TachoCalibration => "tacho_calibration", DocumentType.CommunityLicence => "community_licence", _ => type.ToString().ToLowerInvariant() };
    private static bool CanAct(Document document, AccessContext actor) => document.Status == DocumentStatus.Uploaded && ((document.OwnerType == DocumentOwnerType.Employee && PermissionMatrix.Resolve("documents.driver.verify", actor.Roles)?.Cell == PermissionMatrix.Cell.Full) || (document.OwnerType == DocumentOwnerType.Vehicle && PermissionMatrix.Resolve("documents.vehicle.manage", actor.Roles)?.Cell == PermissionMatrix.Cell.Full));
    private DocumentListCursor? Unprotect(string? value) { if (string.IsNullOrWhiteSpace(value)) return null; try { var c = JsonSerializer.Deserialize<DocumentListCursor>(protector.Unprotect(value)); return c is null || c.Id == Guid.Empty ? throw new DocumentInvalidCursorException() : c; } catch (DocumentInvalidCursorException) { throw; } catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException) { throw new DocumentInvalidCursorException(); } }
}
public sealed class DocumentDetailQueryHandler(IConfiguration configuration) : IQueryHandler<GetDocumentQuery, DocumentDetail>
{
    public async Task<DocumentDetail> HandleAsync(GetDocumentQuery query, AccessContext actor, CancellationToken cancellationToken) { var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.")).Options; await using var db = new FleetOpsDbContext(options); var row = await EfDocumentStore.ReadDetailAsync(db, query.SubjectId ?? throw new DocumentValidationException(["id"]), cancellationToken, actor) ?? throw new DocumentConflictException("not_found"); if (actor.Roles.Contains(RoleCode.Dispatcher) && row.OwnerType == DocumentOwnerType.Employee) throw new DocumentConflictException("not_found"); return row; }
}
public sealed class DocumentCountsQueryHandler(IConfiguration configuration, ICompanyTimeService companyTime, TimeProvider time, ISettings settings, IReadiness readiness) : IQueryHandler<GetDocumentCountsQuery, DocumentCounts>
{
    public async Task<DocumentCounts> HandleAsync(GetDocumentCountsQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.")).Options;
        await using var db = new FleetOpsDbContext(options);
        var rows = db.Documents.AsNoTracking().AsQueryable();
        if (actor.Roles.Contains(RoleCode.Dispatcher)) rows = rows.Where(x => x.OwnerType == DocumentOwnerType.Vehicle);
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        var queue = await rows.CountAsync(x => x.Status == DocumentStatus.Uploaded, cancellationToken);
        var expiringRows = rows.Where(x => false);
        foreach (var catalog in DocumentsCatalog.All.Where(x => x.HasExpiry))
        {
            var days = await settings.GetIntAsync(new SettingKey($"documents.lead_days.{DocumentTypeKey(catalog.Type)}"), null, cancellationToken);
            expiringRows = expiringRows.Concat(rows.Where(x => x.Status == DocumentStatus.Verified && x.Type == catalog.Type && x.ExpiresAt >= today && x.ExpiresAt <= today.AddDays(days)));
        }
        var expiring = await expiringRows.CountAsync(cancellationToken);
        var expired = await rows.CountAsync(x => x.Status == DocumentStatus.Verified && x.ExpiresAt < today, cancellationToken);
        var missing = await MissingDocumentQueryHandler.CountAsync(db, actor, readiness, today, cancellationToken);
        return new(queue, expiring, expired, missing);
    }
    private static string DocumentTypeKey(DocumentType type) => type switch { DocumentType.LicenceC_CE => "licence_c_ce", DocumentType.DriverCard => "driver_card", DocumentType.IdCard => "id_card", DocumentType.AdrCertificate => "adr_certificate", DocumentType.HuReport => "hu_report", DocumentType.SpReport => "sp_report", DocumentType.TachoCalibration => "tacho_calibration", DocumentType.CommunityLicence => "community_licence", _ => type.ToString().ToLowerInvariant() };
}

public sealed class MissingDocumentQueryHandler(IConfiguration configuration, IReadiness readiness, ICompanyTimeService companyTime, TimeProvider time) : IQueryHandler<ListMissingDocumentsQuery, DocumentMissingResult>
{
    public async Task<DocumentMissingResult> HandleAsync(ListMissingDocumentsQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.")).Options;
        await using var db = new FleetOpsDbContext(options);
        var all = await LoadAsync(db, actor, readiness, companyTime.ToCompanyDate(time.GetUtcNow()), cancellationToken);
        var offset = ParseCursor(query.Cursor);
        var items = all.Skip(offset).Take(Math.Clamp(query.Limit, 1, 100)).ToList();
        var next = offset + items.Count < all.Count ? (offset + items.Count).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return new(items, next);
    }

    internal static async Task<int> CountAsync(FleetOpsDbContext db, AccessContext actor, IReadiness readiness, DateOnly date, CancellationToken cancellationToken)
        => (await LoadAsync(db, actor, readiness, date, cancellationToken)).Count;

    private static async Task<List<DocumentMissingItem>> LoadAsync(FleetOpsDbContext db, AccessContext actor, IReadiness readiness, DateOnly date, CancellationToken cancellationToken)
    {
        var vehicles = await db.Vehicles.AsNoTracking().Where(x => x.Status == VehicleStatus.Active).OrderBy(x => x.Plate).ToListAsync(cancellationToken);
        var vehicleReadiness = await readiness.ForVehiclesAsync(vehicles.Select(x => x.Id).ToArray(), date, cancellationToken);
        var items = vehicles.SelectMany(vehicle => vehicleReadiness[vehicle.Id].Causes.Where(cause => cause.Code == ReadinessCauseCode.DocumentMissing && cause.DocumentType is not null).Select(cause => new DocumentMissingItem(DocumentOwnerType.Vehicle, vehicle.Id, vehicle.Plate, ParseType(cause.DocumentType!)))).ToList();
        if (!actor.Roles.Contains(RoleCode.Dispatcher))
        {
            var employees = await db.Set<Employee>().AsNoTracking().Where(x => x.Status != EmployeeStatus.Left && x.Roles.Contains("driver")).OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToListAsync(cancellationToken);
            var driverReadiness = await readiness.ForDriversAsync(employees.Select(x => x.Id).ToArray(), date, cancellationToken);
            items.AddRange(employees.SelectMany(employee => driverReadiness[employee.Id].Causes.Where(cause => cause.Code == ReadinessCauseCode.DocumentMissing && cause.DocumentType is not null).Select(cause => new DocumentMissingItem(DocumentOwnerType.Employee, employee.Id, employee.FirstName + " " + employee.LastName, ParseType(cause.DocumentType!)))));
        }
        return items;
    }

    private static int ParseCursor(string? cursor) => int.TryParse(cursor, out var value) && value >= 0 ? value : 0;
    private static DocumentType ParseType(string value) => value switch
    {
        "licence_c_ce" => DocumentType.LicenceC_CE,
        "driver_card" => DocumentType.DriverCard,
        "code95" => DocumentType.Code95,
        "medical" => DocumentType.Medical,
        "adr_certificate" => DocumentType.AdrCertificate,
        "id_card" => DocumentType.IdCard,
        "employment_contract" => DocumentType.EmploymentContract,
        "registration" => DocumentType.Registration,
        "hu_report" => DocumentType.HuReport,
        "sp_report" => DocumentType.SpReport,
        "tacho_calibration" => DocumentType.TachoCalibration,
        "insurance" => DocumentType.Insurance,
        "community_licence" => DocumentType.CommunityLicence,
        _ => throw new InvalidOperationException("unknown_document_type"),
    };
}
public sealed class SearchDocumentsQueryHandler(IConfiguration configuration) : IQueryHandler<SearchDocumentsQuery, DocumentSearchResult>
{
    public async Task<DocumentSearchResult> HandleAsync(SearchDocumentsQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Search) || query.Search.Trim().Length < 2) return new([], [], []);
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.")).Options;
        await using var db = new FleetOpsDbContext(options);
        var search = query.Search.Trim().Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
        var documents = db.Documents.AsNoTracking().Where(x => EF.Functions.ILike(x.Number ?? "", $"%{search}%", "\\") || (x.OwnerType == DocumentOwnerType.Employee && db.Set<Employee>().Any(e => e.Id == x.OwnerId && EF.Functions.ILike(e.FirstName + " " + e.LastName, $"%{search}%", "\\"))) || (x.OwnerType == DocumentOwnerType.Vehicle && db.Vehicles.Any(v => v.Id == x.OwnerId && EF.Functions.ILike(v.Plate, $"%{search}%", "\\"))));
        if (actor.Roles.Contains(RoleCode.Dispatcher)) documents = documents.Where(x => x.OwnerType == DocumentOwnerType.Vehicle);
        var rows = await documents.OrderByDescending(x => x.CreatedAt).Take(5).ToListAsync(cancellationToken);
        var employeeIds = rows.Where(x => x.OwnerType == DocumentOwnerType.Employee).Select(x => x.OwnerId).ToArray();
        var vehicleIds = rows.Where(x => x.OwnerType == DocumentOwnerType.Vehicle).Select(x => x.OwnerId).ToArray();
        var employees = await db.Set<Employee>().Where(x => employeeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName, cancellationToken);
        var vehicles = await db.Vehicles.Where(x => vehicleIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var docs = rows.Select(x => new DocumentSearchItem(x.Id, x.Type, x.Number, x.OwnerType == DocumentOwnerType.Employee ? employees.GetValueOrDefault(x.OwnerId, "") : vehicles.GetValueOrDefault(x.OwnerId)?.Plate ?? "")).ToList();
        var vehicleRows = await db.Vehicles.AsNoTracking().Where(x => EF.Functions.ILike(x.Plate, $"%{search}%", "\\") || EF.Functions.ILike(x.Make, $"%{search}%", "\\") || EF.Functions.ILike(x.Model, $"%{search}%", "\\")).OrderBy(x => x.Plate).Take(5).Select(x => new VehicleSearchItem(x.Id, x.Plate, x.Make, x.Model)).ToListAsync(cancellationToken);
        var people = actor.Roles.Contains(RoleCode.Dispatcher) ? [] : await db.Set<Employee>().AsNoTracking().Where(x => x.Status != EmployeeStatus.Left && EF.Functions.ILike(x.FirstName + " " + x.LastName, $"%{search}%", "\\")).OrderBy(x => x.LastName).Take(5).Select(x => new PeopleSearchItem(x.Id, x.FirstName + " " + x.LastName)).ToListAsync(cancellationToken);
        return new(docs, vehicleRows, people);
    }
}
public sealed class DocumentFileLinkQueryHandler(IConfiguration configuration, IObjectStore objects) : IQueryHandler<GetDocumentFileLinkQuery, DocumentFileLinkResult>
{
    public async Task<DocumentFileLinkResult> HandleAsync(GetDocumentFileLinkQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.")).Options;
        await using var db = new FleetOpsDbContext(options);
        var document = await EfDocumentStore.ReadDetailAsync(db, query.DocumentId, cancellationToken, actor) ?? throw new DocumentConflictException("not_found");
        if (actor.Roles.Contains(RoleCode.Dispatcher) && document.OwnerType == DocumentOwnerType.Employee) throw new DocumentConflictException("not_found");
        var key = await db.DocumentFiles.Where(x => x.DocumentId == query.DocumentId && x.Id == query.FileId).Select(x => x.ObjectKey).SingleOrDefaultAsync(cancellationToken) ?? throw new DocumentConflictException("not_found");
        return new(await objects.CreateDownloadLinkAsync(key, TimeSpan.FromMinutes(5), cancellationToken));
    }
}
public sealed class PeopleLookupQueryHandler(IConfiguration configuration) : IQueryHandler<LookupPeopleQuery, PeopleLookupResult>
{
    public async Task<PeopleLookupResult> HandleAsync(LookupPeopleQuery query, AccessContext actor, CancellationToken cancellationToken) { var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.")).Options; await using var db = new FleetOpsDbContext(options); var rows = db.Set<Employee>().AsNoTracking().Where(x => x.Status != EmployeeStatus.Left); if (!string.IsNullOrWhiteSpace(query.Search)) { var search = query.Search.Trim(); if (Guid.TryParse(search, out var id)) rows = rows.Where(x => x.Id == id); else { var escapedSearch = search.Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal); rows = rows.Where(x => EF.Functions.ILike(x.FirstName + " " + x.LastName, $"%{escapedSearch}%", "\\")); } } return new(await rows.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).Take(20).Select(x => new PeopleLookupItem(x.Id, x.FirstName + " " + x.LastName)).ToListAsync(cancellationToken)); }
}
