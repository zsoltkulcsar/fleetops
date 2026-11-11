using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;
using FleetOps.Modules.Documents;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using FleetOps.Kernel;
using FleetOps.Modules.Notifications;

namespace FleetOps.Migrations;

public sealed class EfDocumentStore(IObjectStore objects, IConfiguration configuration, ILogger<EfDocumentStore> logger, ICompanyTimeService companyTime, INotifications notifications, TimeProvider? time = null, IChangedSubjects? changedSubjects = null) : IDocumentStore
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private static readonly HashSet<string> ContentTypes = ["image/jpeg", "image/png", "image/webp", "image/heic", "application/pdf"];
    public async Task<UploadLinkResult> RequestAsync(Guid ownerId, DocumentOwnerType ownerType, DocumentType type, string contentType, long sizeBytes, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        ValidateType(ownerType, type);
        if (!ContentTypes.Contains(contentType)) throw new DocumentValidationException(["contentType"]);
        var max = configuration.GetValue<long>("Storage:MaxFileBytes", 15728640);
        if (sizeBytes is < 1 || sizeBytes > max) throw new DocumentValidationException(["sizeBytes"]);
        await using var db = CreateDb(context, actor.EmployeeId);
        await ValidateOwnerAsync(db, ownerType, ownerId, type, cancellationToken);
        if (actor.Roles.Contains(RoleCode.Driver) && await db.Documents.AnyAsync(document => document.OwnerType == ownerType && document.OwnerId == ownerId && document.Type == type && document.Status == DocumentStatus.Uploaded, cancellationToken))
            throw new DocumentConflictException("upload_pending");
        var now = clock.GetUtcNow();
        var expiration = now.AddMinutes(configuration.GetValue("Storage:LinkMinutes", 5));
        var pending = new PendingFile { ObjectKey = $"documents/{Guid.CreateVersion7()}", ActorId = actor.EmployeeId, ContentType = contentType, MaxSizeBytes = sizeBytes, CreatedAt = now, ExpiresAt = expiration };
        db.PendingFiles.Add(pending);
        await db.SaveChangesAsync(cancellationToken);
        var link = await objects.CreateUploadLinkAsync(pending.ObjectKey, contentType, expiration - now, cancellationToken);
        return new(pending.Id, link, expiration, new UploadRequiredHeaders(contentType));
    }
    public async Task<DocumentDetail> ConfirmAsync(Guid ownerId, DocumentOwnerType ownerType, DocumentType type, string? number, DateOnly? issuedAt, DateOnly? expiresAt, string[]? categories, IReadOnlyList<ConfirmFileInput> files, AccessContext actor, CommandContext context, CancellationToken cancellationToken, Guid? commandId = null)
    {
        ValidateType(ownerType, type);
        if (expiresAt < issuedAt) throw new DocumentValidationException(["expiresAt"]);
        if (type != DocumentType.LicenceC_CE && categories is { Length: > 0 }) throw new DocumentValidationException(["categories"]);
        var catalog = DocumentsCatalog.Get(type);
        if (files.Count is < 1 or > 10 || (catalog.TwoSided && (files.Count != 2 || files.Select(x => x.Page).OrderBy(x => x).SequenceEqual([DocumentFilePage.Front, DocumentFilePage.Back]) is false)) || (!catalog.TwoSided && files.Any(x => x.Page != DocumentFilePage.Page))) throw new DocumentValidationException(["files"]);
        await using var db = CreateDb(context, actor.EmployeeId);
        await ValidateOwnerAsync(db, ownerType, ownerId, type, cancellationToken);
        if (commandId is Guid replayId)
        {
            var replayLock = $"document-command:{replayId}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({replayLock}, 0))", cancellationToken);
            var existing = await db.Documents.AsNoTracking().SingleOrDefaultAsync(document => document.Id == replayId, cancellationToken);
            if (existing is not null)
            {
                if (existing.OwnerId != ownerId || existing.OwnerType != ownerType || existing.CreatedBy != actor.EmployeeId || existing.Type != type)
                    throw new DocumentConflictException("command_id_reused");
                // The document is the durable acknowledgement. Return it even if
                // HR has already reviewed it or the original upload link expired.
                return await ReadDetailAsync(db, replayId, cancellationToken) ?? throw new DocumentConflictException("not_found");
            }
        }
        if (actor.Roles.Contains(RoleCode.Driver))
        {
            var lockKey = $"{ownerType}:{ownerId}:{type}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
            if (await db.Documents.AnyAsync(document => document.OwnerType == ownerType && document.OwnerId == ownerId && document.Type == type && document.Status == DocumentStatus.Uploaded, cancellationToken)) throw new DocumentConflictException("upload_pending");
        }
        var fileIds = files.Select(file => file.FileId).ToArray();
        var pending = await db.PendingFiles.FromSqlInterpolated($"SELECT * FROM fleetops.pending_files WHERE id = ANY({fileIds}) ORDER BY id FOR UPDATE").ToListAsync(cancellationToken);
        if (pending.Count != files.Count || pending.Any(x => x.ConfirmedAt.HasValue || x.Purpose != "document")) throw new DocumentValidationException([], "file_not_found");
        foreach (var input in files)
        {
            var row = pending.Single(x => x.Id == input.FileId);
            var info = await objects.GetInfoAsync(row.ObjectKey, cancellationToken);
            if (row.ActorId != actor.EmployeeId || row.ExpiresAt <= clock.GetUtcNow() || info is null || info.ContentType != row.ContentType || info.SizeBytes < 1 || info.SizeBytes > row.MaxSizeBytes)
            {
                try { await objects.DeleteAsync(row.ObjectKey, cancellationToken); } catch (Exception ex) { logger.LogWarning("Object cleanup failed with exception type {ExceptionType}", ex.GetType().Name); }
                throw new DocumentValidationException([], "file_rejected");
            }
        }
        var document = new Document { OwnerType = ownerType, OwnerId = ownerId, Type = type, Number = number, IssuedAt = issuedAt, ExpiresAt = expiresAt, Categories = categories ?? [], CreatedBy = actor.EmployeeId, CreatedAt = clock.GetUtcNow() };
        if (commandId is Guid documentId) document.Id = documentId;
        db.Documents.Add(document);
        for (var i = 0; i < files.Count; i++) { var input = files[i]; var row = pending.Single(x => x.Id == input.FileId); var info = await objects.GetInfoAsync(row.ObjectKey, cancellationToken) ?? throw new DocumentValidationException([], "file_rejected"); row.ConfirmedAt = clock.GetUtcNow(); document.Files.Add(new DocumentFile { DocumentId = document.Id, ObjectKey = row.ObjectKey, ContentType = row.ContentType, SizeBytes = info.SizeBytes, Page = input.Page, SortOrder = i }); }
        await db.SaveChangesAsync(cancellationToken);
        changedSubjects?.Add(document.OwnerType == DocumentOwnerType.Employee ? "employee" : "vehicle", document.OwnerId);
        changedSubjects?.Add("document", document.Id);
        return await ReadDetailAsync(db, document.Id, cancellationToken) ?? throw new DocumentConflictException("not_found");
    }
    public async Task<DocumentDetail> VerifyAsync(Guid id, int version, DateOnly? expiresAt, DateOnly? issuedAt, string? number, string[]? categories, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = CreateDb(context, actor.EmployeeId);
        var document = await LockedDocumentAsync(db, id, cancellationToken) ?? throw new DocumentConflictException("not_found");
        if (document.Version != version) throw new DocumentConflictException("version_conflict");
        if (document.Status != DocumentStatus.Uploaded) throw new DocumentConflictException("state_conflict");
        var catalog = DocumentsCatalog.Get(document.Type);
        if (catalog.HasExpiry && expiresAt is null) throw new DocumentValidationException(["expiresAt"]);
        if (expiresAt < issuedAt) throw new DocumentValidationException(["expiresAt"]);
        if (document.Type != DocumentType.LicenceC_CE && categories is { Length: > 0 }) throw new DocumentValidationException(["categories"]);
        var lockKey = $"{document.OwnerType}:{document.OwnerId}:{document.Type}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        var previous = await db.Documents.FromSqlInterpolated($"SELECT * FROM fleetops.documents WHERE owner_id = {document.OwnerId} AND owner_type = {document.OwnerType.ToString()} AND type = {document.Type.ToString().ToSnakeCase()} AND status = {"Verified"} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        if (previous is not null) { previous.Status = DocumentStatus.Replaced; previous.Version++; }
        document.Status = DocumentStatus.Verified;
        document.VerifiedBy = actor.EmployeeId;
        document.VerifiedAt = clock.GetUtcNow();
        document.ExpiresAt = expiresAt;
        document.IssuedAt = issuedAt;
        document.Number = number;
        document.Categories = categories ?? [];
        document.ReplacesId = previous?.Id;
        document.Version++;
        await db.SaveChangesAsync(cancellationToken);
        changedSubjects?.Add(document.OwnerType == DocumentOwnerType.Employee ? "employee" : "vehicle", document.OwnerId);
        changedSubjects?.Add("document", document.Id);
        return await ReadDetailAsync(db, id, cancellationToken, actor) ?? throw new DocumentConflictException("not_found");
    }
    public async Task<DocumentDetail> RejectAsync(Guid id, int version, string reason, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DocumentValidationException(["reason"]);
        await using var db = CreateDb(context, actor.EmployeeId);
        var document = await LockedDocumentAsync(db, id, cancellationToken) ?? throw new DocumentConflictException("not_found");
        if (document.Version != version) throw new DocumentConflictException("version_conflict");
        if (document.Status != DocumentStatus.Uploaded) throw new DocumentConflictException("state_conflict");
        document.Status = DocumentStatus.Rejected;
        document.RejectReason = reason.Trim();
        document.Version++;
        await db.SaveChangesAsync(cancellationToken);
        await notifications.AddAsync(document.CreatedBy, "document_rejected", new { documentId = document.Id, documentType = document.Type.ToString().ToSnakeCase() }, null, context, cancellationToken);
        changedSubjects?.Add(document.OwnerType == DocumentOwnerType.Employee ? "employee" : "vehicle", document.OwnerId);
        changedSubjects?.Add("document", document.Id);
        return await ReadDetailAsync(db, id, cancellationToken, actor) ?? throw new DocumentConflictException("not_found");
    }
    public async Task<DocumentDetail> DeleteAsync(Guid id, int version, string reason, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DocumentValidationException(["reason"]);
        await using var db = CreateDb(context, actor.EmployeeId);
        var document = await LockedDocumentAsync(db, id, cancellationToken) ?? throw new DocumentConflictException("not_found");
        if (document.Version != version) throw new DocumentConflictException("version_conflict");
        var files = await db.DocumentFiles.Where(x => x.DocumentId == id).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);
        foreach (var file in files) await objects.DeleteAsync(file.ObjectKey, cancellationToken);
        if (document.ReplacesId is Guid replacesId)
        {
            var previous = await db.Documents.SingleOrDefaultAsync(x => x.Id == replacesId && x.Status == DocumentStatus.Replaced, cancellationToken);
            if (previous is not null) { previous.Status = DocumentStatus.Verified; previous.Version++; }
        }
        db.DocumentFiles.RemoveRange(files);
        db.Documents.Remove(document);
        db.AuditEntries.Add(new FleetOps.Modules.Audit.AuditEntry { Id = Guid.CreateVersion7(), EntityType = nameof(Document), EntityId = id, FieldName = "deleted", ActorId = actor.EmployeeId.ToString(), Reason = $"type={document.Type};owner={document.OwnerType}:{document.OwnerId};reason={reason.Trim()}", CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
        changedSubjects?.Add(document.OwnerType == DocumentOwnerType.Employee ? "employee" : "vehicle", document.OwnerId);
        changedSubjects?.Add("document", document.Id);
        return new DocumentDetail(id, document.OwnerType, document.OwnerId, "", document.Type, null, null, null, document.Status, document.CreatedAt, version, null, null, [], [], false);
    }
    public async Task<DocumentDetail> SightCheckAsync(Guid id, int version, DateOnly checkedAt, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var today = companyTime.ToCompanyDate(clock.GetUtcNow());
        if (checkedAt > today) throw new DocumentValidationException(["checkedAt"]);
        await using var db = CreateDb(context, actor.EmployeeId);
        var document = await LockedDocumentAsync(db, id, cancellationToken) ?? throw new DocumentConflictException("not_found");
        if (document.Version != version) throw new DocumentConflictException("version_conflict");
        if (document.Type != DocumentType.LicenceC_CE || document.Status != DocumentStatus.Verified) throw new DocumentConflictException("state_conflict");
        document.LastSightCheckAt = checkedAt;
        document.Version++;
        await db.SaveChangesAsync(cancellationToken);
        changedSubjects?.Add(document.OwnerType == DocumentOwnerType.Employee ? "employee" : "vehicle", document.OwnerId);
        changedSubjects?.Add("document", document.Id);
        return await ReadDetailAsync(db, id, cancellationToken, actor) ?? throw new DocumentConflictException("not_found");
    }
    internal static void ValidateType(DocumentOwnerType ownerType, DocumentType type) { var catalog = DocumentsCatalog.Get(type); if (catalog.OwnerType != ownerType) throw new DocumentValidationException(["type"]); }
    private static async Task ValidateOwnerAsync(FleetOpsDbContext db, DocumentOwnerType ownerType, Guid ownerId, DocumentType type, CancellationToken cancellationToken)
    { if (ownerType == DocumentOwnerType.Employee) { if (!await db.Set<Employee>().AnyAsync(x => x.Id == ownerId && x.Status != EmployeeStatus.Left, cancellationToken)) throw new DocumentConflictException("owner_not_found"); } else { var vehicle = await db.Vehicles.SingleOrDefaultAsync(x => x.Id == ownerId, cancellationToken) ?? throw new DocumentConflictException("owner_not_found"); if (vehicle.Status == FleetOps.Modules.Fleet.VehicleStatus.Retired) throw new DocumentConflictException("state_conflict"); if (DocumentsCatalog.Get(type).TruckOnly && vehicle.Kind != FleetOps.Modules.Fleet.VehicleKind.Truck) throw new DocumentValidationException(["type"]); } }
    private FleetOpsDbContext CreateDb(CommandContext context, Guid actorId) { var options = new DbContextOptionsBuilder<FleetOpsDbContext>(); MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection); var db = new FleetOpsDbContext(options.Options) { AuditActorId = actorId }; db.Database.UseTransaction(context.DbTransaction); return db; }
    public static async Task<DocumentDetail?> ReadDetailAsync(FleetOpsDbContext db, Guid id, CancellationToken cancellationToken, AccessContext? actor = null)
    {
        return await db.Documents.AsNoTracking().Where(x => x.Id == id).Select(x => new DocumentDetail(
            x.Id,
            x.OwnerType,
            x.OwnerId,
            x.OwnerType == DocumentOwnerType.Employee
                ? db.Set<Employee>().Where(e => e.Id == x.OwnerId).Select(e => e.FirstName + " " + e.LastName).FirstOrDefault() ?? ""
                : db.Vehicles.Where(v => v.Id == x.OwnerId).Select(v => v.Plate).FirstOrDefault() ?? "",
            x.Type,
            x.Number,
            x.IssuedAt,
            x.ExpiresAt,
            x.Status,
            x.CreatedAt,
            x.Version,
            x.VerifiedAt,
            x.RejectReason,
            x.Categories,
            x.Files.OrderBy(f => f.SortOrder).Select(f => new DocumentFileDetail(f.Id, f.Page, f.ContentType, f.SizeBytes)).ToList(),
            actor != null && ((x.OwnerType == DocumentOwnerType.Vehicle && PermissionMatrix.Resolve("documents.vehicle.manage", actor.Roles)!.Cell == PermissionMatrix.Cell.Full) || (x.OwnerType == DocumentOwnerType.Employee && PermissionMatrix.Resolve("documents.driver.verify", actor.Roles)!.Cell == PermissionMatrix.Cell.Full)) && x.Status == DocumentStatus.Uploaded)).SingleOrDefaultAsync(cancellationToken);
    }
    private static Task<Document?> LockedDocumentAsync(FleetOpsDbContext db, Guid id, CancellationToken cancellationToken) => db.Documents.FromSqlInterpolated($"SELECT * FROM fleetops.documents WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
}
