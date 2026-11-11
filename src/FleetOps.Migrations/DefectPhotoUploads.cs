using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;
using FleetOps.Modules.Documents;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfDefectPhotoUploads(IObjectStore objects, IConfiguration configuration, TimeProvider time) : IDefectPhotoUploads
{
    public async Task<UploadLinkResult> RequestAsync(string contentType, long sizeBytes, Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        if (contentType is not ("image/jpeg" or "image/png" or "image/webp" or "image/heic"))
            throw new DocumentValidationException(["contentType"]);
        if (sizeBytes < 1 || sizeBytes > configuration.GetValue<long>("Storage:MaxFileBytes", 15728640))
            throw new DocumentValidationException(["sizeBytes"]);
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        await using var db = new FleetOpsDbContext(options.Options);
        db.Database.UseTransaction(context.DbTransaction);
        if (!await db.Employees.AnyAsync(employee => employee.Id == actorId && employee.Status == FleetOps.People.EmployeeStatus.Active, cancellationToken))
            throw new DocumentConflictException("state_conflict");
        var now = time.GetUtcNow();
        var expiration = now.AddMinutes(configuration.GetValue("Storage:LinkMinutes", 5));
        var file = new PendingFile { ObjectKey = $"defects/{Guid.CreateVersion7()}", ActorId = actorId, Purpose = "defect_photo", ContentType = contentType, MaxSizeBytes = sizeBytes, CreatedAt = now, ExpiresAt = expiration };
        db.PendingFiles.Add(file);
        await db.SaveChangesAsync(cancellationToken);
        var link = await objects.CreateUploadLinkAsync(file.ObjectKey, contentType, expiration - now, cancellationToken);
        return new(file.Id, link, expiration, new UploadRequiredHeaders(contentType));
    }
}
