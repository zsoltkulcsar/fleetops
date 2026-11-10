using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Migrations;
using FleetOps.Modules.Documents;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FleetOps.Tests;

public sealed class DriverUploadReplayTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private Guid employeeId;
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.InitializeAsync();
        employeeId = await AddEmployee();
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Driver_upload_replay_after_lost_response_returns_same_document_without_duplicate_files_or_audit()
    {
        using var client = factory.AsDriver(employeeId);
        var fileId = await Upload(client);
        var commandId = Guid.NewGuid();
        var request = Request(commandId, fileId);
        using var original = await client.PostAsJsonAsync("/api/v1/driver/documents", request, Cancellation);
        Assert.Equal(HttpStatusCode.Created, original.StatusCode);
        var originalDocument = await original.Content.ReadFromJsonAsync<DriverDocumentDetail>(VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(commandId, originalDocument!.Id);
        var auditCount = await factory.CountRows("audit_entries");
        factory.Clock.Advance(TimeSpan.FromDays(15));
        using var replay = await client.PostAsJsonAsync("/api/v1/driver/documents", request, Cancellation);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var replayedDocument = await replay.Content.ReadFromJsonAsync<DriverDocumentDetail>(VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(commandId, replayedDocument!.Id);
        Assert.Equal(originalDocument.Files[0].Id, Assert.Single(replayedDocument.Files).Id);
        await using var db = Database();
        var document = Assert.Single(await db.Documents.ToListAsync(Cancellation));
        Assert.Equal(employeeId, document.OwnerId);
        Assert.Equal(1, document.Version);
        Assert.Equal(1L, await factory.CountRows("document_files"));
        Assert.Equal(auditCount, await factory.CountRows("audit_entries"));
    }

    [Fact]
    public async Task Concurrent_driver_confirmations_with_one_command_id_create_one_document()
    {
        using var client = factory.AsDriver(employeeId);
        var fileId = await Upload(client);
        var commandId = Guid.NewGuid();
        var request = Request(commandId, fileId);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/v1/driver/documents", request, Cancellation)));
        try
        {
            foreach (var response in responses)
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<DriverDocumentDetail>(VehicleApiFactory.JsonOptions, Cancellation);
                Assert.Equal(commandId, result!.Id);
            }
            await using var db = Database();
            Assert.Equal(commandId, Assert.Single(await db.Documents.ToListAsync(Cancellation)).Id);
            Assert.Equal(1L, await factory.CountRows("document_files"));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Driver_cannot_reuse_another_drivers_command_id()
    {
        var commandId = Guid.NewGuid();
        using (var first = factory.AsDriver(employeeId))
        {
            var fileId = await Upload(first);
            using var created = await first.PostAsJsonAsync("/api/v1/driver/documents", Request(commandId, fileId), Cancellation);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        var otherEmployee = await AddEmployee();
        using var other = factory.AsDriver(otherEmployee);
        var otherFile = await Upload(other);
        using var refused = await other.PostAsJsonAsync("/api/v1/driver/documents", Request(commandId, otherFile), Cancellation);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("command_id_reused", (await refused.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("code").GetString());
        await using var db = Database();
        var document = Assert.Single(await db.Documents.ToListAsync(Cancellation));
        Assert.Equal(employeeId, document.OwnerId);
        Assert.False((await db.PendingFiles.SingleAsync(file => file.Id == otherFile, Cancellation)).ConfirmedAt.HasValue);
    }

    [Fact]
    public async Task Driver_upload_with_empty_command_id_returns_field_validation_without_writing()
    {
        using var client = factory.AsDriver(employeeId);
        var fileId = await Upload(client);
        using var response = await client.PostAsJsonAsync("/api/v1/driver/documents", Request(Guid.Empty, fileId), Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("validation_error", error.GetProperty("code").GetString());
        Assert.Contains("commandId", error.GetRawText());
        await using var db = Database();
        Assert.False(await db.Documents.AnyAsync(Cancellation));
        Assert.Null((await db.PendingFiles.SingleAsync(file => file.Id == fileId, Cancellation)).ConfirmedAt);
    }

    [Fact]
    public async Task Driver_replay_after_hr_review_preserves_office_decision()
    {
        var commandId = Guid.NewGuid();
        Guid fileId;
        using (var client = factory.AsDriver(employeeId))
        {
            fileId = await Upload(client);
            using var created = await client.PostAsJsonAsync("/api/v1/driver/documents", Request(commandId, fileId), Cancellation);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        using (var hr = factory.AsHr())
        {
            using var rejected = await hr.PostAsJsonAsync($"/api/v1/documents/{commandId}/reject", new { version = 1, reason = "Please retake the photo" }, Cancellation);
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        }
        using var retry = factory.AsDriver(employeeId);
        using var response = await retry.PostAsJsonAsync("/api/v1/driver/documents", Request(commandId, fileId), Cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var replayed = await response.Content.ReadFromJsonAsync<DriverDocumentDetail>(VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(DocumentStatus.Uploaded, replayed!.Status);
        await using var db = Database();
        var reviewed = Assert.Single(await db.Documents.ToListAsync(Cancellation));
        Assert.Equal(2, reviewed.Version);
        Assert.Equal(DocumentStatus.Rejected, reviewed.Status);
        Assert.Equal("Please retake the photo", reviewed.RejectReason);
        Assert.Equal(1L, await factory.CountRows("document_files"));
    }

    private object Request(Guid commandId, Guid fileId) => new { commandId, type = "code95", expiresAt = "2027-10-01", files = new[] { new { fileId, page = "page" } } };

    [Fact]
    public async Task Replaying_a_lost_confirmation_cannot_recreate_a_misupload_deleted_by_hr()
    {
        var commandId = Guid.NewGuid();
        Guid fileId;
        using (var client = factory.AsDriver(employeeId))
        {
            fileId = await Upload(client);
            using var created = await client.PostAsJsonAsync("/api/v1/driver/documents", Request(commandId, fileId), Cancellation);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        using (var hr = factory.AsHr())
        {
            using var deleted = await hr.PostAsJsonAsync($"/api/v1/documents/{commandId}/delete", new { version = 1, reason = "Wrong photo" }, Cancellation);
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        }
        using var retry = factory.AsDriver(employeeId);
        using var replayed = await retry.PostAsJsonAsync("/api/v1/driver/documents", Request(commandId, fileId), Cancellation);
        Assert.Equal(HttpStatusCode.Created, replayed.StatusCode);
        Assert.Equal(commandId, (await replayed.Content.ReadFromJsonAsync<DriverDocumentDetail>(VehicleApiFactory.JsonOptions, Cancellation))!.Id);
        await using var db = Database();
        Assert.False(await db.DocumentFiles.AnyAsync(Cancellation));
        Assert.Single(await db.CommandReceipts.Where(receipt => receipt.CommandId == commandId && receipt.EmployeeId == employeeId).ToListAsync(Cancellation));
        Assert.Equal(1, factory.ObjectStore.Linked.Count);
    }

    private async Task<Guid> Upload(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/driver/documents/upload-links", new { type = "code95", contentType = "image/jpeg", sizeBytes = 12 }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var grant = await response.Content.ReadFromJsonAsync<UploadLinkResult>(VehicleApiFactory.JsonOptions, Cancellation);
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "image/jpeg");
        return grant!.FileId;
    }

    private async Task<Guid> AddEmployee()
    {
        var id = Guid.NewGuid();
        await using var db = Database();
        db.Employees.Add(new Employee { Id = id, FirstName = "Offline", LastName = "Fake", Email = $"{id:N}@example.invalid", Roles = "[\"driver\"]", StartDate = new DateOnly(2026, 1, 1) });
        await db.SaveChangesAsync(Cancellation);
        return id;
    }

    private FleetOpsDbContext Database()
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, factory.RuntimeConnectionString);
        return new FleetOpsDbContext(options.Options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = "offline_upload_setup" };
    }
}
