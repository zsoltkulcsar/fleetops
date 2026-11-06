using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;
using FleetOps.Modules.Documents;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfDocumentStore(IObjectStore objects, IConfiguration configuration, ILogger<EfDocumentStore> logger, TimeProvider? time = null) : IDocumentStore
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
        var now = clock.GetUtcNow();
        var expiration = now.AddMinutes(configuration.GetValue("Storage:LinkMinutes", 5));
        var pending = new PendingFile { ObjectKey = $"documents/{Guid.CreateVersion7()}", ActorId = actor.EmployeeId, ContentType = contentType, MaxSizeBytes = sizeBytes, CreatedAt = now, ExpiresAt = expiration };
        db.PendingFiles.Add(pending);
        await db.SaveChangesAsync(cancellationToken);
        var link = await objects.CreateUploadLinkAsync(pending.ObjectKey, contentType, expiration - now, cancellationToken);
        return new(pending.Id, link, expiration, new UploadRequiredHeaders(contentType));
    }
    public async Task<DocumentDetail> ConfirmAsync(Guid ownerId, DocumentOwnerType ownerType, DocumentType type, string? number, DateOnly? issuedAt, DateOnly? expiresAt, string[]? categories, IReadOnlyList<ConfirmFileInput> files, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        ValidateType(ownerType, type);
        if (expiresAt < issuedAt) throw new DocumentValidationException(["expiresAt"]);
        if (type != DocumentType.LicenceC_CE && categories is { Length: > 0 }) throw new DocumentValidationException(["categories"]);
        var catalog = DocumentsCatalog.Get(type);
        if (files.Count is < 1 or > 10 || (catalog.TwoSided && (files.Count != 2 || files.Select(x => x.Page).OrderBy(x => x).SequenceEqual([DocumentFilePage.Front, DocumentFilePage.Back]) is false)) || (!catalog.TwoSided && files.Any(x => x.Page != DocumentFilePage.Page))) throw new DocumentValidationException(["files"]);
        await using var db = CreateDb(context, actor.EmployeeId);
        await ValidateOwnerAsync(db, ownerType, ownerId, type, cancellationToken);
        var pending = await db.PendingFiles.Where(x => files.Select(y => y.FileId).Contains(x.Id)).ToListAsync(cancellationToken);
        if (pending.Count != files.Count || pending.Any(x => x.ConfirmedAt.HasValue)) throw new DocumentValidationException([], "file_not_found");
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
        db.Documents.Add(document);
        for (var i = 0; i < files.Count; i++) { var input = files[i]; var row = pending.Single(x => x.Id == input.FileId); var info = await objects.GetInfoAsync(row.ObjectKey, cancellationToken) ?? throw new DocumentValidationException([], "file_rejected"); row.ConfirmedAt = clock.GetUtcNow(); document.Files.Add(new DocumentFile { DocumentId = document.Id, ObjectKey = row.ObjectKey, ContentType = row.ContentType, SizeBytes = info.SizeBytes, Page = input.Page, SortOrder = i }); }
        await db.SaveChangesAsync(cancellationToken);
        return await ReadDetailAsync(db, document.Id, cancellationToken) ?? throw new DocumentConflictException("not_found");
    }
    internal static void ValidateType(DocumentOwnerType ownerType, DocumentType type) { var catalog = DocumentsCatalog.Get(type); if (catalog.OwnerType != ownerType) throw new DocumentValidationException(["type"]); }
    private static async Task ValidateOwnerAsync(FleetOpsDbContext db, DocumentOwnerType ownerType, Guid ownerId, DocumentType type, CancellationToken cancellationToken)
    { if (ownerType == DocumentOwnerType.Employee) { if (!await db.Set<Employee>().AnyAsync(x => x.Id == ownerId && x.Status != EmployeeStatus.Left, cancellationToken)) throw new DocumentConflictException("owner_not_found"); } else { var vehicle = await db.Vehicles.SingleOrDefaultAsync(x => x.Id == ownerId, cancellationToken) ?? throw new DocumentConflictException("owner_not_found"); if (vehicle.Status == FleetOps.Modules.Fleet.VehicleStatus.Retired) throw new DocumentConflictException("state_conflict"); if (DocumentsCatalog.Get(type).TruckOnly && vehicle.Kind != FleetOps.Modules.Fleet.VehicleKind.Truck) throw new DocumentValidationException(["type"]); } }
    private FleetOpsDbContext CreateDb(CommandContext context, Guid actorId) { var options = new DbContextOptionsBuilder<FleetOpsDbContext>(); MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection); var db = new FleetOpsDbContext(options.Options) { AuditActorId = actorId }; db.Database.UseTransaction(context.DbTransaction); return db; }
    public static async Task<DocumentDetail?> ReadDetailAsync(FleetOpsDbContext db, Guid id, CancellationToken cancellationToken)
    { return await db.Documents.AsNoTracking().Where(x => x.Id == id).Select(x => new DocumentDetail(x.Id, x.OwnerType, x.OwnerId, x.OwnerType == DocumentOwnerType.Employee ? db.Set<Employee>().Where(e => e.Id == x.OwnerId).Select(e => e.FirstName + " " + e.LastName).FirstOrDefault() ?? "" : db.Vehicles.Where(v => v.Id == x.OwnerId).Select(v => v.Plate).FirstOrDefault() ?? "", x.Type, x.Number, x.IssuedAt, x.ExpiresAt, x.Status, x.CreatedAt, x.Version, x.VerifiedAt, x.RejectReason, x.Categories, x.Files.OrderBy(f => f.SortOrder).Select(f => new DocumentFileDetail(f.Id, f.Page, f.ContentType, f.SizeBytes)).ToList())).SingleOrDefaultAsync(cancellationToken); }
}
