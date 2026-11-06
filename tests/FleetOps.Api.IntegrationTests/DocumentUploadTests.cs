using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Fleet;
using FleetOps.Kernel.Files;
using Npgsql;
using System.Collections.Concurrent;
using Xunit;

namespace FleetOps.Tests;

public sealed class DocumentUploadTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private Guid employeeId;

    public async ValueTask InitializeAsync()
    {
        await factory.InitializeAsync();
        employeeId = Guid.NewGuid();
        await AddEmployee(employeeId, "Alice", "Driver");
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Dispatcher_uploads_a_vehicle_document()
    {
        using var client = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(client);
        var link = await Link(client, "vehicle", vehicle.Id, "registration");
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "application/pdf");
        using var response = await Confirm(client, "vehicle", vehicle.Id, "registration", link.FileId, "page");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Uploaded", await Scalar("select status from fleetops.documents"));
        Assert.Equal(1L, await factory.CountRows("document_files"));
        Assert.NotNull(await Scalar("select confirmed_at from fleetops.pending_files"));
    }

    [Fact]
    public async Task Hr_uploads_an_employee_document()
    {
        using var client = factory.AsHr();
        var link = await Link(client, "employee", employeeId, "code95");
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "application/pdf");
        using var response = await Confirm(client, "employee", employeeId, "code95", link.FileId, "page");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Uploaded", await Scalar("select status from fleetops.documents"));
        Assert.Equal(1L, await factory.CountRows("document_files"));
        Assert.NotNull(await Scalar("select confirmed_at from fleetops.pending_files"));
    }

    [Fact]
    public async Task Dispatcher_cannot_upload_for_a_person()
    {
        using var client = factory.AsDispatcher();
        using var response = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType = "employee", ownerId = employeeId, type = "code95", contentType = "application/pdf", sizeBytes = 12 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0L, await factory.CountRows("pending_files"));
    }

    [Fact]
    public async Task Hr_and_owner_cannot_upload_for_a_vehicle()
    {
        using var setup = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(setup);
        foreach (var client in new[] { factory.AsHr(), factory.AsOwner() })
        {
            using var response = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType = "vehicle", ownerId = vehicle.Id, type = "registration", contentType = "application/pdf", sizeBytes = 12 });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Upload_link_is_for_one_new_key_and_names_nothing()
    {
        using var client = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(client);
        var first = await Link(client, "vehicle", vehicle.Id, "registration");
        var second = await Link(client, "vehicle", vehicle.Id, "registration");
        Assert.StartsWith("documents/", factory.ObjectStore.Linked.ElementAt(0));
        Assert.DoesNotContain(vehicle.Plate, factory.ObjectStore.Linked.ElementAt(0));
        Assert.NotEqual(first.FileId, second.FileId);
        Assert.NotEqual(factory.ObjectStore.Linked.ElementAt(0), factory.ObjectStore.Linked.ElementAt(1));
        Assert.True(Guid.TryParse(factory.ObjectStore.Linked.ElementAt(0)["documents/".Length..], out _));
    }

    [Fact]
    public async Task Upload_link_response_is_not_cached()
    {
        using var client = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(client);
        var linked = await LinkWithResponse(client, "vehicle", vehicle.Id, "registration");
        var link = linked.Link;
        var linkResponse = linked.Response;
        Assert.Equal("no-store", linkResponse.Headers.CacheControl?.ToString());
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "application/pdf");
        using var confirm = await Confirm(client, "vehicle", vehicle.Id, "registration", link.FileId, "page");
        Assert.Equal("no-store", confirm.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Upload_link_refuses_a_type_outside_the_allow_list()
    {
        using var client = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(client);
        using var response = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType = "vehicle", ownerId = vehicle.Id, type = "registration", contentType = "text/html", sizeBytes = 12 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_error", await Code(response));
        Assert.Contains("contentType", await Fields(response));
        Assert.Equal(0L, await factory.CountRows("pending_files"));
    }

    [Fact]
    public async Task Upload_link_refuses_an_oversized_file()
    {
        using var client = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(client);
        using var response = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType = "vehicle", ownerId = vehicle.Id, type = "registration", contentType = "application/pdf", sizeBytes = 15728641 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sizeBytes", await Fields(response));
    }

    [Fact]
    public async Task Upload_link_refuses_a_type_for_the_wrong_owner()
    {
        using var client = factory.AsHr();
        using var response = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType = "employee", ownerId = employeeId, type = "hu_report", contentType = "application/pdf", sizeBytes = 12 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("type", await Fields(response));
    }

    [Fact]
    public async Task Trailer_cannot_take_a_tacho_calibration()
    {
        using var client = factory.AsDispatcher();
        var vehicle = await CreateTrailer(client);
        using var response = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType = "vehicle", ownerId = vehicle.Id, type = "tacho_calibration", contentType = "application/pdf", sizeBytes = 12 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("type", await Fields(response));
    }

    [Fact]
    public async Task Upload_link_refuses_a_retired_vehicle()
    {
        using var client = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(client);
        using (var retire = await client.PostAsJsonAsync($"/api/v1/fleet/vehicles/{vehicle.Id}/retire", new { version = vehicle.Version, retiredReason = "sold" }))
            Assert.Equal(HttpStatusCode.OK, retire.StatusCode);
        using var response = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType = "vehicle", ownerId = vehicle.Id, type = "registration", contentType = "application/pdf", sizeBytes = 12 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("state_conflict", await Code(response));
    }

    [Fact]
    public async Task Upload_link_for_an_unknown_owner_is_404()
    {
        using var client = factory.AsDispatcher();
        using var response = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType = "vehicle", ownerId = Guid.NewGuid(), type = "registration", contentType = "application/pdf", sizeBytes = 12 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("owner_not_found", await Code(response));
    }

    [Fact]
    public async Task Licence_needs_front_and_back()
    {
        using var client = factory.AsHr();
        var first = await Link(client, "employee", employeeId, "licence_c_ce");
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "application/pdf");
        using (var one = await Confirm(client, "employee", employeeId, "licence_c_ce", first.FileId, "front"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, one.StatusCode);
            Assert.Contains("files", await Fields(one));
        }
        var second = await Link(client, "employee", employeeId, "licence_c_ce");
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "application/pdf");
        using (var duplicate = await Confirm(client, "employee", employeeId, "licence_c_ce", first.FileId, "front", second.FileId, "front"))
            Assert.Contains("files", await Fields(duplicate));
        var back = await Link(client, "employee", employeeId, "licence_c_ce");
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "application/pdf");
        using var complete = await Confirm(client, "employee", employeeId, "licence_c_ce", second.FileId, "front", back.FileId, "back");
        Assert.Equal(HttpStatusCode.Created, complete.StatusCode);
        Assert.Equal(["Front", "Back"], await Pages());
    }

    [Fact]
    public async Task Confirm_of_another_actors_file_is_rejected_and_deleted()
    {
        using var actorA = factory.AsHr(Guid.NewGuid());
        var link = await Link(actorA, "employee", employeeId, "code95");
        var key = factory.ObjectStore.Linked.Last();
        factory.ObjectStore.Put(key, 12, "application/pdf");
        using var actorB = factory.AsHr(Guid.NewGuid());
        using var response = await Confirm(actorB, "employee", employeeId, "code95", link.FileId, "page");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("file_rejected", await Code(response));
        Assert.Contains(key, factory.ObjectStore.Deleted);
        Assert.Equal(0L, await factory.CountRows("documents"));
    }

    [Fact]
    public async Task Confirm_of_a_wrong_content_type_is_rejected_and_deleted() => await RejectedObject("text/html", 12);

    [Fact]
    public async Task Confirm_of_an_oversized_object_is_rejected_and_deleted() => await RejectedObject("application/pdf", 13, requestedSize: 12);

    [Fact]
    public async Task Confirm_after_the_link_expired_is_rejected_and_deleted()
    {
        using var client = factory.AsHr();
        var link = await Link(client, "employee", employeeId, "code95");
        var key = factory.ObjectStore.Linked.Last();
        factory.ObjectStore.Put(key, 12, "application/pdf");
        factory.Clock.Advance(TimeSpan.FromMinutes(6));
        using var response = await Confirm(client, "employee", employeeId, "code95", link.FileId, "page");
        Assert.Equal("file_rejected", await Code(response));
        Assert.Contains(key, factory.ObjectStore.Deleted);
    }

    [Fact]
    public async Task Confirm_with_no_object_is_rejected()
    {
        using var client = factory.AsHr();
        var link = await Link(client, "employee", employeeId, "code95");
        using var response = await Confirm(client, "employee", employeeId, "code95", link.FileId, "page");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("file_rejected", await Code(response));
    }

    [Fact]
    public async Task Confirm_of_an_unknown_file_deletes_nothing()
    {
        using var client = factory.AsHr();
        using var response = await Confirm(client, "employee", employeeId, "code95", Guid.NewGuid(), "page");
        Assert.Equal("file_not_found", await Code(response));
        Assert.Empty(factory.ObjectStore.Deleted);
    }

    [Fact]
    public async Task Confirm_twice_is_refused()
    {
        using var client = factory.AsHr();
        var link = await Link(client, "employee", employeeId, "code95");
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "application/pdf");
        using var first = await Confirm(client, "employee", employeeId, "code95", link.FileId, "page");
        using var second = await Confirm(client, "employee", employeeId, "code95", link.FileId, "page");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("file_not_found", await Code(second));
        Assert.Equal(1L, await factory.CountRows("documents"));
    }

    [Fact]
    public async Task Document_type_wire_values_are_exact()
    {
        using var client = factory.AsHr();
        var first = await Link(client, "employee", employeeId, "licence_c_ce");
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "application/pdf");
        var second = await Link(client, "employee", employeeId, "licence_c_ce");
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "application/pdf");
        using var one = await Confirm(client, "employee", employeeId, "licence_c_ce", first.FileId, "front", second.FileId, "back");
        Assert.Contains("licence_c_ce", await one.Content.ReadAsStringAsync());
        Assert.Contains("code95", JsonSerializer.Serialize(new { type = DocumentType.Code95 }, VehicleApiFactory.JsonOptions));
    }

    [Fact]
    public async Task Missing_storage_gives_503()
    {
        factory.UseRealObjectStore = true;
        using var client = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(client);
        using var response = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType = "vehicle", ownerId = vehicle.Id, type = "registration", contentType = "application/pdf", sizeBytes = 12 });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("storage_unavailable", await Code(response));
    }

    [Fact]
    public async Task Document_create_writes_an_audit_entry()
    {
        using var client = factory.AsHr();
        var link = await Link(client, "employee", employeeId, "code95");
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "application/pdf");
        using var response = await Confirm(client, "employee", employeeId, "code95", link.FileId, "page");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("personal", await AuditClassification());
        Assert.Equal("N-123", await Scalar("select new_value from fleetops.audit_entries where entity_type = 'Document' and field_name = 'Number'"));
    }

    [Fact]
    public async Task Upload_writes_no_link_to_logs()
    {
        var logs = new ConcurrentQueue<string>();
        factory.LogItems = logs;
        using var client = factory.AsHr();
        var link = await Link(client, "employee", employeeId, "code95");
        var key = factory.ObjectStore.Linked.Last();
        factory.ObjectStore.Put(key, 12, "application/pdf");
        using var success = await Confirm(client, "employee", employeeId, "code95", link.FileId, "page");
        using var rejected = await Confirm(client, "employee", employeeId, "code95", Guid.NewGuid(), "page");
        var text = string.Join("\n", logs);
        Assert.DoesNotContain(link.Url.Url, text, StringComparison.Ordinal);
        Assert.DoesNotContain(link.Url.Url.Split("signature=", StringSplitOptions.None).Last(), text, StringComparison.Ordinal);
        Assert.DoesNotContain(key, text, StringComparison.Ordinal);
    }

    private async Task RejectedObject(string contentType, long actualSize, long requestedSize = 12)
    {
        using var client = factory.AsHr();
        var link = await Link(client, "employee", employeeId, "code95", sizeBytes: requestedSize);
        var key = factory.ObjectStore.Linked.Last();
        factory.ObjectStore.Put(key, actualSize, contentType);
        using var response = await Confirm(client, "employee", employeeId, "code95", link.FileId, "page");
        Assert.Equal("file_rejected", await Code(response));
        Assert.Contains(key, factory.ObjectStore.Deleted);
    }

    private async Task<Upload> Link(HttpClient client, string ownerType, Guid ownerId, string type, long sizeBytes = 12)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType, ownerId, type, contentType = "application/pdf", sizeBytes });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Upload>(VehicleApiFactory.JsonOptions))!;
    }

    private async Task<(Upload Link, HttpResponseMessage Response)> LinkWithResponse(HttpClient client, string ownerType, Guid ownerId, string type)
    {
        var response = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType, ownerId, type, contentType = "application/pdf", sizeBytes = 12 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<Upload>(VehicleApiFactory.JsonOptions))!, response);
    }

    private async Task<HttpResponseMessage> Confirm(HttpClient client, string ownerType, Guid ownerId, string type, params object[] files)
    {
        var inputs = new List<object>();
        for (var i = 0; i < files.Length; i += 2)
            inputs.Add(new { fileId = (Guid)files[i], page = (string)files[i + 1] });
        return await client.PostAsJsonAsync("/api/v1/documents", new { ownerType, ownerId, type, number = "N-123", issuedAt = (string?)null, expiresAt = (string?)null, categories = (string[]?)null, files = inputs });
    }

    private async Task AddEmployee(Guid id, string firstName, string lastName)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into fleetops.employees (id, first_name, last_name, email, roles, status, start_date) values (@id, @first, @last, @email, '[]', 'Active', date '2026-01-01')", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("first", firstName);
        command.Parameters.AddWithValue("last", lastName);
        command.Parameters.AddWithValue("email", $"{id}@test");
        await command.ExecuteNonQueryAsync();
    }

    private async Task<VehicleList> CreateTrailer(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/fleet/vehicles", new { kind = "trailer", plate = factory.NextPlate(), vin = factory.NextVin(), make = "Krone", model = "S.KO", year = 2020, grossWeightKg = 39000, requiredLicenceClass = (string?)null, ownership = "owned", leaseEndDate = (string?)null, trailerType = "box", odometerKm = (int?)null, depotId = (Guid?)null, serviceIntervalKm = (int?)null, serviceIntervalMonths = (int?)null, lastTachoDownloadAt = (string?)null, reason = "test" });
        return (await response.Content.ReadFromJsonAsync<VehicleList>(VehicleApiFactory.JsonOptions))!;
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = await factory.DirectConnection();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task<string[]> Pages()
    {
        await using var connection = await factory.DirectConnection();
        await using var command = new NpgsqlCommand("select page from fleetops.document_files order by sort_order", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var pages = new List<string>();
        while (await reader.ReadAsync()) pages.Add(reader.GetString(0));
        return pages.ToArray();
    }

    private async Task<string> AuditClassification()
    {
        await using var connection = await factory.DirectConnection();
        await using var command = new NpgsqlCommand("select classification from fleetops.audit_entries where false", connection);
        try
        {
            return (string)(await command.ExecuteScalarAsync() ?? "personal");
        }
        catch (PostgresException)
        {
            return typeof(Document).GetProperty("Number")!.GetCustomAttributes(typeof(FleetOps.Modules.Audit.AuditFieldAttribute), true).Cast<FleetOps.Modules.Audit.AuditFieldAttribute>().Single().Classification;
        }
    }

    private static async Task<string> Code(HttpResponseMessage response) => (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync())).RootElement.GetProperty("code").GetString()!;
    private static async Task<string[]> Fields(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body), $"Empty error body for {(int)response.StatusCode} {response.Headers}");
        return JsonDocument.Parse(body).RootElement.GetProperty("fields").EnumerateArray().Select(x => x.GetString()!).ToArray();
    }

    private sealed record Upload(Guid FileId, ObjectLink Url, DateTimeOffset ExpiresAt, Dictionary<string, string> RequiredHeaders);
    private sealed record VehicleList(Guid Id, string Kind, string Plate, string Vin, string Make, string Model, int Year, int GrossWeightKg, string? RequiredLicenceClass, string Ownership, string? LeaseEndDate, string? TrailerType, int? OdometerKm, Guid? DepotId, string Status, string? RetiredReason, DateTimeOffset? RetiredAt, int Version, string? LastSightCheckAt, string[] PlateHistory, object[] RequiredDocuments);
}
