using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Kernel.Files;
using FleetOps.Migrations;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Views;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FleetOps.Tests;

public sealed class EpicThreeTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private static readonly DateOnly Today = new(2026, 11, 6);
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;
    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Schedule_lists_by_truck_and_driver_and_manual_edits_notify_both_affected_drivers()
    {
        using var client = factory.AsDispatcher();
        var truck = await factory.CreateTruck(client);
        var firstDriver = await Employee("First");
        var secondDriver = await Employee("Second");
        using var added = await client.PostAsJsonAsync("/api/v1/schedule/cycles", new { employeeId = firstDriver, vehicleId = truck.Id, from = Today, to = Today.AddDays(10), reason = "Stand-in" }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var cycle = await added.Content.ReadFromJsonAsync<CycleDetail>(VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(CycleSource.Manual, cycle!.Source);
        Assert.Null(cycle.PatternSlot);
        using var edited = await client.PutAsJsonAsync($"/api/v1/schedule/cycles/{cycle.Id}", new { version = cycle.Version, employeeId = secondDriver, vehicleId = truck.Id, from = Today.AddDays(2), to = Today.AddDays(12), reason = "Replacement driver" }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var schedule = await client.GetFromJsonAsync<ScheduleResult>($"/api/v1/schedule?from={Today:yyyy-MM-dd}&to={Today.AddDays(20):yyyy-MM-dd}&employeeId={secondDriver}&vehicleId={truck.Id}", VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(cycle.Id, Assert.Single(schedule!.Items).Id);
        Assert.Equal(2, schedule.Items[0].Version);
        Assert.Equal("Second Fake", schedule.Items[0].EmployeeName);
        await using var db = Database();
        Assert.Equal(2, await db.Notifications.CountAsync(notification => notification.EmployeeId == firstDriver && notification.TemplateCode == "schedule_changed", Cancellation));
        Assert.Equal(1, await db.Notifications.CountAsync(notification => notification.EmployeeId == secondDriver && notification.TemplateCode == "schedule_changed", Cancellation));
        Assert.Equal(secondDriver, (await db.Cycles.SingleAsync(row => row.Id == cycle.Id, Cancellation)).EmployeeId);
    }

    [Fact]
    public async Task Editing_a_generated_cycle_keeps_its_slot_and_a_stale_edit_does_not_apply()
    {
        using var client = factory.AsDispatcher();
        var truck = await factory.CreateTruck(client);
        var otherTruck = await factory.CreateTruck(client);
        var employeeId = await Employee("Pattern");
        using var saved = await client.PutAsJsonAsync($"/api/v1/people/{employeeId}/cycle-pattern", new { version = 0, pattern = new CyclePattern(2, 2, Today, truck.Id) }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var pattern = await client.GetFromJsonAsync<CyclePatternView>($"/api/v1/people/{employeeId}/cycle-pattern", VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(1, pattern!.Version);
        Assert.Equal(truck.Id, pattern.Pattern!.VehicleId);
        await using var db = Database();
        var cycle = await db.Cycles.SingleAsync(row => row.EmployeeId == employeeId && row.PatternSlot == 1, Cancellation);
        var request = new { version = 1, employeeId, vehicleId = otherTruck.Id, from = cycle.From.AddDays(2), to = cycle.To.AddDays(2), reason = "Tour adjusted" };
        using var edit = await client.PutAsJsonAsync($"/api/v1/schedule/cycles/{cycle.Id}", request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var stale = await client.PutAsJsonAsync($"/api/v1/schedule/cycles/{cycle.Id}", request, Cancellation);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("version_conflict", (await stale.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("code").GetString());
        db.ChangeTracker.Clear();
        var current = await db.Cycles.SingleAsync(row => row.Id == cycle.Id, Cancellation);
        Assert.Equal(1, current.PatternSlot);
        Assert.Equal(CycleSource.Manual, current.Source);
        Assert.Equal(2, current.Version);
        Assert.Equal(otherTruck.Id, current.VehicleId);
    }

    [Fact]
    public async Task Retiring_a_truck_lists_and_cancels_future_cycles_and_tells_drivers()
    {
        using var client = factory.AsDispatcher();
        var truck = await factory.CreateTruck(client);
        var employeeId = await Employee("Retire");
        var cycleId = await Cycle(employeeId, truck.Id, Today.AddDays(3), Today.AddDays(10));
        var impact = await client.GetFromJsonAsync<VehicleRetirementImpact>($"/api/v1/fleet/vehicles/{truck.Id}/retirement-impact", VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(cycleId, Assert.Single(impact!.Items).Id);
        using var response = await client.PostAsJsonAsync($"/api/v1/fleet/vehicles/{truck.Id}/retire", new { version = truck.Version, retiredReason = "sold" }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = Database();
        var cycle = await db.Cycles.SingleAsync(row => row.Id == cycleId, Cancellation);
        Assert.Equal(CycleStatus.Cancelled, cycle.Status);
        Assert.NotNull(cycle.FinishedAt);
        Assert.Equal(2, cycle.Version);
        Assert.Equal(1, await db.Notifications.CountAsync(notification => notification.EmployeeId == employeeId && notification.TemplateCode == "schedule_changed", Cancellation));
    }

    [Fact]
    public async Task Hr_can_read_schedule_but_cannot_add_a_cycle()
    {
        using var setup = factory.AsDispatcher();
        var truck = await factory.CreateTruck(setup);
        var employeeId = await Employee("Readonly");
        using var client = factory.AsHr();
        using var read = await client.GetAsync($"/api/v1/schedule?from={Today:yyyy-MM-dd}&to={Today.AddDays(14):yyyy-MM-dd}", Cancellation);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var write = await client.PostAsJsonAsync("/api/v1/schedule/cycles", new { employeeId, vehicleId = truck.Id, from = Today, to = Today.AddDays(10), reason = "Refused" }, Cancellation);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal("permission_denied", (await write.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("code").GetString());
        await using var db = Database();
        Assert.False(await db.Cycles.AnyAsync(Cancellation));
    }

    [Fact]
    public async Task Driver_snapshot_has_only_own_allowlisted_data_and_no_files_or_links()
    {
        using var setup = factory.AsDispatcher();
        var truck = await factory.CreateTruck(setup);
        var own = await Employee("Own");
        var other = await Employee("OtherSecretName");
        var ownCycle = await Cycle(own, truck.Id, Today, Today.AddDays(13));
        await Cycle(other, truck.Id, Today.AddDays(28), Today.AddDays(41));
        var ownDocument = await Document(own, DocumentType.Medical, DocumentStatus.Rejected, rejectReason: "Photo unreadable");
        await Document(other, DocumentType.Code95, DocumentStatus.Verified);
        using var client = factory.AsDriver(own);
        using var response = await client.GetAsync("/api/v1/driver/snapshot", Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var raw = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.DoesNotContain("OtherSecretName", raw);
        Assert.DoesNotContain("private-number", raw);
        Assert.DoesNotContain("object-secret-key", raw);
        var snapshot = await response.Content.ReadFromJsonAsync<DriverSnapshot>(VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(own, snapshot!.EmployeeId);
        Assert.Equal(factory.Clock.GetUtcNow(), snapshot.GeneratedAt);
        Assert.Equal(ownCycle, Assert.Single(snapshot.Cycles).Id);
        Assert.Equal(ownDocument, Assert.Single(snapshot.Documents).Id);
        Assert.Equal("Photo unreadable", snapshot.Documents[0].RejectReason);
        using var json = JsonDocument.Parse(raw);
        Assert.Equal(new[] { "expiresAt", "id", "rejectReason", "status", "type" }, json.RootElement.GetProperty("documents")[0].EnumerateObject().Select(property => property.Name).Order());
        Assert.DoesNotContain("files", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https:", raw);
    }

    [Fact]
    public async Task Driver_truck_access_is_limited_to_current_upcoming_or_overrun_cycles()
    {
        using var setup = factory.AsDispatcher();
        var truck = await factory.CreateTruck(setup);
        var own = await Employee("Bounded");
        var cycleId = await Cycle(own, truck.Id, Today.AddDays(-20), Today.AddDays(-1));
        var documentId = await Document(truck.Id, DocumentType.HuReport, DocumentStatus.Verified, vehicle: true);
        using var client = factory.AsDriver(own);
        using var old = await client.GetAsync($"/api/v1/driver/vehicles/{truck.Id}", Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
        Assert.Equal("not_found", (await old.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("code").GetString());
        await using (var db = Database())
        {
            var cycle = await db.Cycles.SingleAsync(row => row.Id == cycleId, Cancellation);
            cycle.Status = CycleStatus.Overrun;
            await db.SaveChangesAsync(Cancellation);
        }
        using var overrun = await client.GetAsync($"/api/v1/driver/vehicles/{truck.Id}", Cancellation);
        Assert.Equal(HttpStatusCode.OK, overrun.StatusCode);
        var detail = await overrun.Content.ReadFromJsonAsync<DriverVehicleDetail>(VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(documentId, Assert.Single(detail!.Documents).Id);
        Assert.Single(detail.Documents[0].Files);
        var raw = await overrun.Content.ReadAsStringAsync(Cancellation);
        Assert.DoesNotContain("private-number", raw);
        Assert.DoesNotContain("object-secret-key", raw);
        await using (var db = Database())
        {
            var cycle = await db.Cycles.SingleAsync(row => row.Id == cycleId, Cancellation);
            cycle.Status = CycleStatus.Cancelled;
            await db.SaveChangesAsync(Cancellation);
        }
        using var cancelled = await client.GetAsync($"/api/v1/driver/vehicles/{truck.Id}", Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, cancelled.StatusCode);
    }

    [Fact]
    public async Task Driver_file_links_are_fresh_online_and_refuse_other_people_and_unassigned_trucks()
    {
        using var setup = factory.AsDispatcher();
        var truck = await factory.CreateTruck(setup);
        var own = await Employee("Files");
        var other = await Employee("Private");
        var otherDocument = await Document(other, DocumentType.Medical, DocumentStatus.Verified);
        var vehicleDocument = await Document(truck.Id, DocumentType.Registration, DocumentStatus.Verified, vehicle: true);
        var ownDocument = await Document(own, DocumentType.Code95, DocumentStatus.Rejected, rejectReason: "Blurred");
        await using var db = Database();
        var otherFile = await db.DocumentFiles.Where(file => file.DocumentId == otherDocument).Select(file => file.Id).SingleAsync(Cancellation);
        var truckFile = await db.DocumentFiles.Where(file => file.DocumentId == vehicleDocument).Select(file => file.Id).SingleAsync(Cancellation);
        var ownFile = await db.DocumentFiles.Where(file => file.DocumentId == ownDocument).Select(file => file.Id).SingleAsync(Cancellation);
        using var client = factory.AsDriver(own);
        using var forbiddenPerson = await client.GetAsync($"/api/v1/driver/documents/{otherDocument}/files/{otherFile}/link", Cancellation);
        using var forbiddenTruck = await client.GetAsync($"/api/v1/driver/documents/{vehicleDocument}/files/{truckFile}/link", Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenPerson.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenTruck.StatusCode);
        Assert.Empty(factory.ObjectStore.DownloadCalls);
        using var ownDetail = await client.GetAsync($"/api/v1/driver/documents/{ownDocument}", Cancellation);
        Assert.Equal(HttpStatusCode.OK, ownDetail.StatusCode);
        Assert.Equal("Blurred", (await ownDetail.Content.ReadFromJsonAsync<DriverDocumentDetail>(VehicleApiFactory.JsonOptions, Cancellation))!.RejectReason);
        using var first = await client.GetAsync($"/api/v1/driver/documents/{ownDocument}/files/{ownFile}/link", Cancellation);
        using var second = await client.GetAsync($"/api/v1/driver/documents/{ownDocument}/files/{ownFile}/link", Cancellation);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("no-store", first.Headers.CacheControl!.ToString());
        Assert.Equal(2, factory.ObjectStore.DownloadCalls.Count);
        await Cycle(own, truck.Id, Today.AddDays(14), Today.AddDays(27));
        using var future = await client.GetAsync($"/api/v1/driver/documents/{vehicleDocument}/files/{truckFile}/link", Cancellation);
        Assert.Equal(HttpStatusCode.OK, future.StatusCode);
        Assert.Equal(3, factory.ObjectStore.DownloadCalls.Count);
    }

    [Fact]
    public async Task Driver_upload_reaches_queue_without_office_fields_and_pending_upload_is_refused()
    {
        var own = await Employee("Uploader");
        var other = await Employee("NotOwner");
        using var client = factory.AsDriver(own);
        using var requested = await client.PostAsJsonAsync("/api/v1/driver/documents/upload-links", new { type = "code95", contentType = "image/jpeg", sizeBytes = 12 }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var link = await requested.Content.ReadFromJsonAsync<UploadLinkResult>(VehicleApiFactory.JsonOptions, Cancellation);
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "image/jpeg");
        using var uploaded = await client.PostAsJsonAsync("/api/v1/driver/documents", new { ownerId = other, type = "code95", expiresAt = Today.AddDays(100), files = new[] { new { fileId = link!.FileId, page = "page" } } }, Cancellation);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var detail = await uploaded.Content.ReadFromJsonAsync<DriverDocumentDetail>(VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(DocumentStatus.Uploaded, detail!.Status);
        Assert.Single(detail.Files);
        await using var db = Database();
        var document = await db.Documents.SingleAsync(row => row.Id == detail.Id, Cancellation);
        Assert.Equal(own, document.OwnerId);
        Assert.Equal(own, document.CreatedBy);
        using var duplicate = await client.PostAsJsonAsync("/api/v1/driver/documents/upload-links", new { type = "code95", contentType = "image/jpeg", sizeBytes = 12 }, Cancellation);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("upload_pending", (await duplicate.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("code").GetString());
        using var hr = factory.AsHr();
        var queue = await hr.GetFromJsonAsync<DocumentListResult>("/api/v1/documents?filter=queue", VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(detail.Id, Assert.Single(queue!.Items).Id);
        using var rejected = await hr.PostAsJsonAsync($"/api/v1/documents/{detail.Id}/reject", new { version = 1, reason = "Photo is blurred" }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        using var driverAgain = factory.AsDriver(own);
        var rejectedDetail = await driverAgain.GetFromJsonAsync<DriverDocumentDetail>($"/api/v1/driver/documents/{detail.Id}", VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(DocumentStatus.Rejected, rejectedDetail!.Status);
        Assert.Equal("Photo is blurred", rejectedDetail.RejectReason);
        using var retry = await driverAgain.PostAsJsonAsync("/api/v1/driver/documents/upload-links", new { type = "code95", contentType = "image/jpeg", sizeBytes = 12 }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var retryLink = await retry.Content.ReadFromJsonAsync<UploadLinkResult>(VehicleApiFactory.JsonOptions, Cancellation);
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "image/jpeg");
        using var retried = await driverAgain.PostAsJsonAsync("/api/v1/driver/documents", new { type = "code95", files = new[] { new { fileId = retryLink!.FileId, page = "page" } } }, Cancellation);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        var renewed = await retried.Content.ReadFromJsonAsync<DriverDocumentDetail>(VehicleApiFactory.JsonOptions, Cancellation);
        Assert.NotEqual(detail.Id, renewed!.Id);
        db.ChangeTracker.Clear();
        Assert.Equal(DocumentStatus.Rejected, (await db.Documents.SingleAsync(row => row.Id == detail.Id, Cancellation)).Status);
        Assert.Equal(DocumentStatus.Uploaded, (await db.Documents.SingleAsync(row => row.Id == renewed.Id, Cancellation)).Status);
    }

    [Fact]
    public async Task Blocked_driver_upload_is_first_in_verify_queue_with_stable_priority_paging()
    {
        var able = await Employee("Able");
        var blocked = await Employee("Blocked");
        foreach (var type in new[] { DocumentType.LicenceC_CE, DocumentType.DriverCard, DocumentType.Code95, DocumentType.Medical })
            await Document(able, type, DocumentStatus.Verified);
        var olderAble = await Document(able, DocumentType.IdCard, DocumentStatus.Uploaded, createdAt: factory.Clock.GetUtcNow().AddDays(-2));
        var newerBlocked = await Document(blocked, DocumentType.Code95, DocumentStatus.Uploaded, createdAt: factory.Clock.GetUtcNow());
        using var client = factory.AsHr();
        var first = await client.GetFromJsonAsync<DocumentListResult>("/api/v1/documents?filter=queue&limit=1", VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(newerBlocked, Assert.Single(first!.Items).Id);
        Assert.NotNull(first.NextCursor);
        var next = await client.GetFromJsonAsync<DocumentListResult>($"/api/v1/documents?filter=queue&limit=1&cursor={Uri.EscapeDataString(first.NextCursor!)}", VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(olderAble, Assert.Single(next!.Items).Id);
        Assert.Null(next.NextCursor);
    }

    private async Task<Guid> Employee(string firstName)
    {
        var id = Guid.CreateVersion7();
        await using var db = Database();
        db.Employees.Add(new Employee { Id = id, FirstName = firstName, LastName = "Fake", Email = $"{id:N}@example.invalid", Roles = "[\"driver\"]", StartDate = Today.AddDays(-50) });
        await db.SaveChangesAsync(Cancellation);
        return id;
    }

    private async Task<Guid> Cycle(Guid employeeId, Guid vehicleId, DateOnly from, DateOnly to)
    {
        await using var db = Database();
        var cycle = new Cycle { EmployeeId = employeeId, VehicleId = vehicleId, From = from, To = to, Source = CycleSource.Manual };
        db.Cycles.Add(cycle);
        await db.SaveChangesAsync(Cancellation);
        return cycle.Id;
    }

    private async Task<Guid> Document(Guid ownerId, DocumentType type, DocumentStatus status, bool vehicle = false, string? rejectReason = null, DateTimeOffset? createdAt = null)
    {
        await using var db = Database();
        var document = new Document { OwnerType = vehicle ? DocumentOwnerType.Vehicle : DocumentOwnerType.Employee, OwnerId = ownerId, Type = type, Number = "private-number", Status = status, ExpiresAt = Today.AddDays(200), RejectReason = rejectReason, CreatedBy = VehicleApiFactory.ActorId, CreatedAt = createdAt ?? factory.Clock.GetUtcNow() };
        document.Files.Add(new DocumentFile { DocumentId = document.Id, ObjectKey = "object-secret-key", ContentType = "application/pdf", SizeBytes = 12, Page = DocumentFilePage.Page });
        db.Documents.Add(document);
        await db.SaveChangesAsync(Cancellation);
        return document.Id;
    }

    private FleetOpsDbContext Database()
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, factory.RuntimeConnectionString);
        return new FleetOpsDbContext(options.Options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = "epic_three_setup" };
    }
}
