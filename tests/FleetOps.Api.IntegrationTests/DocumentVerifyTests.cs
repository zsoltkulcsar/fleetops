using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Globalization;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

public sealed class DocumentVerifyTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private readonly Guid employeeId = Guid.NewGuid();

    public async ValueTask InitializeAsync()
    {
        await factory.InitializeAsync();
        await AddEmployee(employeeId, "Alice", "Driver");
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Hr_verifies_with_an_expiry_date()
    {
        var logs = new ConcurrentQueue<string>();
        factory.LogItems = logs;
        var id = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using var response = await Verify(id, 1, "2027-01-10");
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{await response.Content.ReadAsStringAsync()}\n{string.Join("\n", logs)}");
        Assert.Equal("Verified", await Scalar("select status from fleetops.documents"));
        Assert.Equal("2027-01-10", ((DateOnly)(await Scalar("select expires_at from fleetops.documents"))!).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Verify_without_a_required_expiry_is_refused()
    {
        var id = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using var response = await Verify(id, 1, null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("expiresAt", await Fields(response));
        Assert.Equal("Uploaded", await Scalar("select status from fleetops.documents"));
    }

    [Fact]
    public async Task Verify_needs_no_expiry_for_a_registration()
    {
        using var dispatcher = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(dispatcher);
        var id = await Upload(dispatcher, "vehicle", vehicle.Id, "registration");
        using var response = await Verify(id, 1, null, dispatcher);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Verified", await Scalar("select status from fleetops.documents"));
    }

    [Fact]
    public async Task Verify_replaces_the_older_verified_document()
    {
        var first = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using (await Verify(first, 1, "2027-01-10")) { }
        var second = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using (var response = await Verify(second, 1, "2027-02-10")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1L, await ScalarLong("select count(*) from fleetops.documents where status = 'Replaced'"));
        Assert.Equal(1L, await ScalarLong("select count(*) from fleetops.documents where status = 'Verified'"));
        Assert.Equal(first, await ScalarGuid("select replaces_id from fleetops.documents where id = @id", second));
    }

    [Fact]
    public async Task Concurrent_verifies_leave_one_verified_document()
    {
        var first = await Upload(factory.AsHr(), "employee", employeeId, "code95", number: "A");
        var second = await Upload(factory.AsHr(), "employee", employeeId, "code95", number: "B");
        var results = await Task.WhenAll(Verify(first, 1, "2027-01-10"), Verify(second, 1, "2027-01-11"));
        Assert.All(results, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1L, await ScalarLong("select count(*) from fleetops.documents where status = 'Verified'"));
        Assert.Equal(1L, await ScalarLong("select count(*) from fleetops.documents where status = 'Replaced'"));
    }

    [Fact]
    public async Task Hr_cannot_verify_a_vehicle_document()
    {
        using var dispatcher = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(dispatcher);
        var id = await Upload(dispatcher, "vehicle", vehicle.Id, "registration");
        using var response = await Verify(id, 1, null, factory.AsHr());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dispatcher_gets_404_acting_on_an_employee_document()
    {
        var id = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using var response = await Verify(id, 1, "2027-01-10", factory.AsDispatcher());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Owner_cannot_verify()
    {
        var id = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using var response = await Verify(id, 1, "2027-01-10", factory.AsOwner());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Verify_with_a_stale_version_conflicts()
    {
        var id = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using var first = await Verify(id, 1, "2027-01-10");
        using var stale = await Verify(id, 1, "2027-01-11");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("version_conflict", await Code(stale));
    }

    [Fact]
    public async Task Verified_document_cannot_be_verified_again()
    {
        var id = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using var first = await Verify(id, 1, "2027-01-10");
        using var second = await Verify(id, 2, "2027-01-11");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("state_conflict", await Code(second));
    }

    [Fact]
    public async Task Reject_keeps_the_older_verified_document_current()
    {
        var older = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using (await Verify(older, 1, "2027-01-10")) { }
        var rejected = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using var response = await Reject(rejected, 1, "illegible");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Rejected", await Scalar("select status from fleetops.documents where id = @id", rejected));
        Assert.Equal("illegible", await Scalar("select reject_reason from fleetops.documents where id = @id", rejected));
        Assert.Equal("Verified", await Scalar("select status from fleetops.documents where id = @id", older));
    }

    [Fact]
    public async Task Reject_needs_a_reason()
    {
        var id = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using var response = await Reject(id, 1, " ");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("reason", await Fields(response));
    }

    [Fact]
    public async Task Hr_deletes_a_misupload_and_the_file_goes()
    {
        var logs = new ConcurrentQueue<string>();
        factory.LogItems = logs;
        var id = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        var keys = await FileKeys(id);
        using var response = await Delete(id, 1, "misupload");
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{await response.Content.ReadAsStringAsync()}\n{string.Join("\n", logs)}");
        Assert.Equal(0L, await ScalarLong("select count(*) from fleetops.documents where id = @id", id));
        Assert.All(keys, key => Assert.Contains(key, factory.ObjectStore.Deleted));
    }

    [Fact]
    public async Task Delete_audit_keeps_no_number_or_key()
    {
        var id = await Upload(factory.AsHr(), "employee", employeeId, "code95", "SECRET-NUMBER");
        var key = (await FileKeys(id)).Single();
        using var response = await Delete(id, 1, "duplicate upload");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var audit = (string?)await Scalar("select reason from fleetops.audit_entries where field_name = 'deleted' and entity_id = @id", id);
        Assert.Contains("duplicate upload", audit);
        Assert.DoesNotContain("SECRET-NUMBER", audit, StringComparison.Ordinal);
        Assert.DoesNotContain(key, audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatcher_cannot_delete()
    {
        using var dispatcher = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(dispatcher);
        var id = await Upload(dispatcher, "vehicle", vehicle.Id, "registration");
        using var response = await Delete(id, 1, "misupload", dispatcher);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1L, await factory.CountRows("documents"));
    }

    [Fact]
    public async Task Delete_restores_the_replaced_document()
    {
        var older = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using (await Verify(older, 1, "2027-01-10")) { }
        var newer = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using (await Verify(newer, 1, "2027-02-10")) { }
        using var response = await Delete(newer, 2, "wrong scan");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Verified", await Scalar("select status from fleetops.documents where id = @id", older));
    }

    [Fact]
    public async Task Delete_fails_cleanly_when_storage_fails()
    {
        var id = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        factory.ObjectStore.ThrowOnDelete = true;
        using var response = await Delete(id, 1, "misupload");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1L, await factory.CountRows("documents"));
        Assert.Equal(1L, await factory.CountRows("document_files"));
    }

    [Fact]
    public async Task Sight_check_stores_its_date()
    {
        var id = await Upload(factory.AsHr(), "employee", employeeId, "licence_c_ce", files: 2);
        using (await Verify(id, 1, "2027-01-10", categories: new[] { "C" })) { }
        using var response = await SightCheck(id, 2, "2026-11-05");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2026-11-05", ((DateOnly)(await Scalar("select last_sight_check_at from fleetops.documents"))!).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Sight_check_in_the_future_is_refused()
    {
        var id = await Upload(factory.AsHr(), "employee", employeeId, "licence_c_ce", files: 2);
        using (await Verify(id, 1, "2027-01-10", categories: new[] { "C" })) { }
        using var response = await SightCheck(id, 2, "2026-11-07");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("checkedAt", await Fields(response));
    }

    [Fact]
    public async Task Sight_check_is_for_a_verified_licence_only()
    {
        var card = await Upload(factory.AsHr(), "employee", employeeId, "driver_card", files: 2);
        using var cardResponse = await SightCheck(card, 1, "2026-11-05");
        Assert.Equal(HttpStatusCode.Conflict, cardResponse.StatusCode);
        Assert.Equal("state_conflict", await Code(cardResponse));
        var uploaded = await Upload(factory.AsHr(), "employee", employeeId, "licence_c_ce", files: 2);
        using var uploadedResponse = await SightCheck(uploaded, 1, "2026-11-05");
        Assert.Equal(HttpStatusCode.Conflict, uploadedResponse.StatusCode);
        Assert.Equal("state_conflict", await Code(uploadedResponse));
    }

    [Fact]
    public async Task File_link_is_not_cached()
    {
        var id = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        var fileId = await ScalarGuid("select id from fleetops.document_files");
        using var response = await factory.AsHr().GetAsync($"/api/v1/documents/{id}/files/{fileId}/link");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task File_link_refuses_a_file_of_another_document()
    {
        var first = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        var second = await Upload(factory.AsHr(), "employee", employeeId, "medical");
        var fileId = await ScalarGuid("select id from fleetops.document_files where document_id = @id", second);
        using var response = await factory.AsHr().GetAsync($"/api/v1/documents/{first}/files/{fileId}/link");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Dispatcher_cannot_get_a_link_to_an_employee_file()
    {
        var id = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        var fileId = await ScalarGuid("select id from fleetops.document_files");
        using var response = await factory.AsDispatcher().GetAsync($"/api/v1/documents/{id}/files/{fileId}/link");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.ObjectStore.DownloadCalls);
    }

    private async Task<Guid> Upload(HttpClient client, string ownerType, Guid ownerId, string type, string number = "N-123", int files = 1)
    {
        var fileIds = new List<Guid>();
        var pages = files == 2 ? new[] { "front", "back" } : new[] { "page" };
        foreach (var page in pages)
        {
            using var linkResponse = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType, ownerId, type, contentType = "application/pdf", sizeBytes = 12 });
            var link = await linkResponse.Content.ReadFromJsonAsync<JsonElement>(VehicleApiFactory.JsonOptions);
            var fileId = link.GetProperty("fileId").GetGuid();
            factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "application/pdf");
            fileIds.Add(fileId);
        }
        var inputs = fileIds.Select((fileId, index) => new { fileId, page = pages[index] }).ToArray();
        using var confirm = await client.PostAsJsonAsync("/api/v1/documents", new { ownerType, ownerId, type, number, issuedAt = (string?)null, expiresAt = (string?)null, categories = (string[]?)null, files = inputs });
        Assert.Equal(HttpStatusCode.Created, confirm.StatusCode);
        return (await confirm.Content.ReadFromJsonAsync<JsonElement>(VehicleApiFactory.JsonOptions)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> Verify(Guid id, int version, string? expiresAt, HttpClient? client = null, string[]? categories = null) => (client ?? factory.AsHr()).PostAsJsonAsync($"/api/v1/documents/{id}/verify", new { version, expiresAt, issuedAt = (string?)null, number = "VERIFIED", categories });
    private Task<HttpResponseMessage> Reject(Guid id, int version, string reason, HttpClient? client = null) => (client ?? factory.AsHr()).PostAsJsonAsync($"/api/v1/documents/{id}/reject", new { version, reason });
    private Task<HttpResponseMessage> Delete(Guid id, int version, string reason, HttpClient? client = null) => (client ?? factory.AsHr()).PostAsJsonAsync($"/api/v1/documents/{id}/delete", new { version, reason });
    private Task<HttpResponseMessage> SightCheck(Guid id, int version, string checkedAt) => factory.AsHr().PostAsJsonAsync($"/api/v1/documents/{id}/sight-check", new { version, checkedAt });

    private async Task<string[]> FileKeys(Guid id)
    {
        await using var connection = await factory.DirectConnection();
        await using var command = new NpgsqlCommand("select object_key from fleetops.document_files where document_id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        var keys = new List<string>();
        while (await reader.ReadAsync()) keys.Add(reader.GetString(0));
        return keys.ToArray();
    }

    private async Task AddEmployee(Guid id, string first, string last)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into fleetops.employees (id, first_name, last_name, email, roles, status, start_date) values (@id, @first, @last, @email, '[]', 'Active', date '2026-01-01')", connection);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("first", first); command.Parameters.AddWithValue("last", last); command.Parameters.AddWithValue("email", $"{id}@test");
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> Scalar(string sql, Guid? id = null)
    {
        await using var connection = await factory.DirectConnection();
        await using var command = new NpgsqlCommand(sql, connection);
        if (id.HasValue) command.Parameters.AddWithValue("id", id.Value);
        return await command.ExecuteScalarAsync();
    }
    private async Task<long> ScalarLong(string sql, Guid? id = null) => Convert.ToInt64(await Scalar(sql, id));
    private async Task<Guid> ScalarGuid(string sql, Guid? id = null) => (Guid)(await Scalar(sql, id))!;
    private static async Task<string> Code(HttpResponseMessage response) => (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync())).RootElement.GetProperty("code").GetString()!;
    private static async Task<string[]> Fields(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("fields").EnumerateArray().Select(x => x.GetString()!).ToArray();
}
