using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Audit;
using FleetOps.Modules.Fleet;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

// Audit, rollback and read-access tests, plus the helpers shared by both halves of the class.
public sealed partial class VehicleTests
{
    // ------------------------------------------------------------------ audit

    [Fact]
    public async Task Create_writes_audit_entries_with_new_values()
    {
        using var client = factory.AsOwner();
        var plate = factory.NextPlate();
        var vin = factory.NextVin();
        var body = ValidCreateBody(plate: plate, vin: vin);
        body["reason"] = "create audit reason";

        using var response = await PostBody(client, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var vehicle = await ReadDetail(response);
        var rows = await AuditRows(vehicle.Id);
        var byField = rows.ToDictionary(row => row.Field);
        Assert.Equal(rows.Count, byField.Count);
        Assert.Equal(plate, byField["Plate"].NewValue);
        Assert.Equal(vin, byField["Vin"].NewValue);
        Assert.Equal("MAN", byField["Make"].NewValue);
        Assert.Equal("TGX", byField["Model"].NewValue);
        Assert.Equal("Truck", byField["Kind"].NewValue);
        Assert.Equal("8000", byField["GrossWeightKg"].NewValue);
        Assert.Equal("1000", byField["OdometerKm"].NewValue);
        Assert.Equal("Active", byField["Status"].NewValue);
        Assert.All(rows, row =>
        {
            Assert.Null(row.OldValue);
            Assert.NotNull(row.NewValue);
            Assert.Equal(VehicleApiFactory.ActorId.ToString(), row.ActorId);
            Assert.Equal("create audit reason", row.Reason);
        });
        Assert.DoesNotContain(rows, row => row.Field is "Id" or "Version" or "CreatedAt" or "UpdatedAt");
        Assert.DoesNotContain(rows, row => row.Field is "LeaseEndDate" or "TrailerType");
    }

    [Fact]
    public async Task Edit_audit_records_old_and_new_values()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);

        await Edit(client, vehicle, make: "DAF", model: "XF", reason: "audit old and new");

        var rows = await AuditRows(vehicle.Id, "audit old and new");
        Assert.Equal(["Make", "Model"], rows.Select(row => row.Field).Order().ToArray());
        var make = rows.Single(row => row.Field == "Make");
        Assert.Equal("MAN", make.OldValue);
        Assert.Equal("DAF", make.NewValue);
        var model = rows.Single(row => row.Field == "Model");
        Assert.Equal("TGX", model.OldValue);
        Assert.Equal("XF", model.NewValue);
        Assert.All(rows, row => Assert.Equal(VehicleApiFactory.ActorId.ToString(), row.ActorId));
    }

    [Fact]
    public async Task Vehicle_change_writes_audit_with_actor_and_reason_in_the_same_transaction()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        const string reason = "same transaction reason";
        await Edit(client, vehicle, make: "DAF", model: "XF", reason: reason);

        var rows = await AuditRows(vehicle.Id, reason);

        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
        {
            Assert.Equal(VehicleApiFactory.ActorId.ToString(), row.ActorId);
            Assert.Equal(reason, row.Reason);
        });
        var vehiclesBefore = await factory.CountRows("vehicles");
        var auditsBefore = await factory.CountRows("audit_entries");
        factory.ThrowAfterWrite = true;
        using var failed = await Post(client);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(vehiclesBefore, await factory.CountRows("vehicles"));
        Assert.Equal(auditsBefore, await factory.CountRows("audit_entries"));
    }

    [Fact]
    public async Task Each_changed_field_gets_one_audit_entry_and_unchanged_fields_none()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        var before = await factory.CountRows("audit_entries");

        await Edit(client, vehicle, make: "DAF", model: "XF", reason: "two fields");

        await using var connection = await factory.DirectConnection();
        await using var query = new NpgsqlCommand("select field_name from fleetops.audit_entries where entity_id = @id order by field_name", connection);
        query.Parameters.AddWithValue("id", vehicle.Id);
        await using var reader = await query.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var fields = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            fields.Add(reader.GetString(0));
        }

        Assert.Equal(4, fields.Count(field => field is "Make" or "Model"));
        Assert.Contains("Plate", fields);
        Assert.Equal(before + 2, await factory.CountRows("audit_entries"));
    }

    [Fact]
    public async Task Special_field_records_only_that_it_changed()
    {
        await using var admin = new NpgsqlConnection(factory.AdminConnectionString);
        await admin.OpenAsync(TestContext.Current.CancellationToken);
        await using var createTable = new NpgsqlCommand(
            """
            create table fleetops.special_fixture_rows (id uuid primary key, label text not null, secret text not null);
            grant select, insert, update on fleetops.special_fixture_rows to fleetops_runtime;
            """,
            admin);
        await createTable.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        var rowId = Guid.CreateVersion7();
        await using (var insertContext = NewFixtureContext("fixture created"))
        {
            insertContext.Rows.Add(new SpecialFixtureRow { Id = rowId, Label = "first", Secret = "s3cret-one" });
            await insertContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var updateContext = NewFixtureContext("fixture updated"))
        {
            var row = await updateContext.Rows.SingleAsync(x => x.Id == rowId, TestContext.Current.CancellationToken);
            row.Label = "second";
            row.Secret = "s3cret-two";
            await updateContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var all = await AuditRows(rowId);
        var created = all.Where(row => row.Reason == "fixture created").ToList();
        var updated = all.Where(row => row.Reason == "fixture updated").ToList();
        Assert.Equal("first", created.Single(row => row.Field == "Label").NewValue);
        Assert.Null(created.Single(row => row.Field == "Secret").NewValue);
        Assert.Equal(["Label", "Secret"], updated.Select(row => row.Field).Order().ToArray());
        var label = updated.Single(row => row.Field == "Label");
        Assert.Equal("first", label.OldValue);
        Assert.Equal("second", label.NewValue);
        var secret = updated.Single(row => row.Field == "Secret");
        Assert.Null(secret.OldValue);
        Assert.Null(secret.NewValue);
        Assert.DoesNotContain(all, row => (row.OldValue ?? "").Contains("s3cret") || (row.NewValue ?? "").Contains("s3cret"));
    }

    [Fact]
    public async Task Audit_without_an_actor_fails_closed()
    {
        using var client = factory.AsOwner();
        var existing = await factory.CreateTruck(client);
        var vehiclesBefore = await factory.CountRows("vehicles");
        var auditBefore = await factory.CountRows("audit_entries");
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, factory.RuntimeConnectionString);
        await using var withoutActor = new FleetOpsDbContext(options.Options) { AuditReason = "no actor" };
        withoutActor.Vehicles.Add(NewVehicle(existing.DepotId));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => withoutActor.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("audit_actor_required", failure.Message);
        Assert.Equal(vehiclesBefore, await factory.CountRows("vehicles"));
        Assert.Equal(auditBefore, await factory.CountRows("audit_entries"));
        await using var withActor = new FleetOpsDbContext(options.Options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = "with actor" };
        withActor.Vehicles.Add(NewVehicle(existing.DepotId));
        await withActor.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(vehiclesBefore + 1, await factory.CountRows("vehicles"));
        await using var connection = await factory.DirectConnection();
        await using var zeroActor = new NpgsqlCommand("select count(*) from fleetops.audit_entries where actor_id = '00000000-0000-0000-0000-000000000000'", connection);
        Assert.Equal(0L, await zeroActor.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Runtime_role_cannot_update_or_delete_audit_entries()
    {
        await using var connection = await factory.DirectConnection();
        await using var insert = new NpgsqlCommand(
            "insert into fleetops.audit_entries (id, entity_type, entity_id, field_name, old_value, new_value, actor_id, reason, created_at) " +
            "values (@id, 'Vehicle', @entity, 'Make', null, 'MAN', 'actor', 'reason', now())",
            connection);
        insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("entity", Guid.CreateVersion7());
        Assert.Equal(1, await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        foreach (var sql in new[] { "update fleetops.audit_entries set reason = 'x'", "delete from fleetops.audit_entries" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("42501", refusal.SqlState);
        }
    }

    // ------------------------------------------------------------------ rollback

    [Fact]
    public async Task Failure_after_the_write_rolls_back_vehicle_and_audit()
    {
        using var client = factory.AsOwner();
        var vehicles = await factory.CountRows("vehicles");
        var audits = await factory.CountRows("audit_entries");
        factory.ThrowAfterWrite = true;

        using var response = await Post(client);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(vehicles, await factory.CountRows("vehicles"));
        Assert.Equal(audits, await factory.CountRows("audit_entries"));
    }

    [Fact]
    public async Task Edit_failure_after_the_write_rolls_back_vehicle_and_audit()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        var auditBefore = await factory.CountRows("audit_entries");
        var historyBefore = await factory.CountRows("vehicle_plate_history");
        factory.ThrowAfterWrite = true;

        using var response = await Put(client, vehicle, plate: "B-H 9996", make: "DAF", reason: "failing edit");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(auditBefore, await factory.CountRows("audit_entries"));
        Assert.Equal(historyBefore, await factory.CountRows("vehicle_plate_history"));
        await using var secondConnection = await factory.DirectConnection();
        await using var read = new NpgsqlCommand("select plate, make, version from fleetops.vehicles where id = @id", secondConnection);
        read.Parameters.AddWithValue("id", vehicle.Id);
        await using (var reader = await read.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(vehicle.Plate, reader.GetString(0));
            Assert.Equal("MAN", reader.GetString(1));
            Assert.Equal(vehicle.Version, reader.GetInt32(2));
        }

        Assert.Empty(await AuditRows(vehicle.Id, "failing edit"));
        using var retry = await Put(client, vehicle, make: "DAF", reason: "retry after rollback");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    // ------------------------------------------------------------------ read access

    [Fact]
    public async Task Anonymous_get_is_refused()
    {
        using var client = factory.Anonymous();

        using var response = await client.GetAsync($"{VehiclesPath}/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("not_signed_in", await Code(response));
    }

    [Fact]
    public async Task Driver_cannot_read_a_vehicle_yet()
    {
        using var owner = factory.AsOwner();
        var vehicle = await factory.CreateTruck(owner);
        using var driver = factory.AsDriver();

        using var response = await driver.GetAsync($"{VehiclesPath}/{vehicle.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("permission_denied", await Code(response));
        Assert.DoesNotContain(vehicle.Plate, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Hr_can_read_a_vehicle()
    {
        using var owner = factory.AsOwner();
        var vehicle = await factory.CreateTruck(owner);
        using var hr = factory.AsHr();

        using var response = await hr.GetAsync($"{VehiclesPath}/{vehicle.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = await ReadDetail(response);
        Assert.Equal(vehicle.Id, read.Id);
        Assert.Equal(vehicle.Plate, read.Plate);
    }

    [Fact]
    public async Task Get_unknown_vehicle_returns_404()
    {
        using var client = factory.AsOwner();

        using var response = await client.GetAsync($"{VehiclesPath}/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await Code(response));
    }

    [Fact]
    public async Task Vehicle_test_host_runs_as_the_runtime_role()
    {
        await using var connection = await factory.DirectConnection();
        await using var whoAmI = new NpgsqlCommand("select current_user", connection);
        await using var owner = new NpgsqlCommand("select tableowner from pg_tables where schemaname = 'fleetops' and tablename = 'vehicles'", connection);

        Assert.Equal("fleetops_runtime", await whoAmI.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        Assert.Equal("fleetops_migrator", await owner.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        Assert.Equal(VehicleStatus.Active, vehicle.Status);
    }

    // ------------------------------------------------------------------ helpers: requests

    private Dictionary<string, object?> ValidCreateBody(
        string? plate = null,
        string? vin = null,
        string make = "MAN",
        int kind = 0,
        int? trailerType = null,
        int? odometerKm = 1000,
        int ownership = 0,
        string? leaseEndDate = null,
        string? reason = "reason") => new()
    {
        ["kind"] = kind,
        ["plate"] = plate ?? factory.NextPlate(),
        ["vin"] = vin ?? factory.NextVin(),
        ["make"] = make,
        ["model"] = "TGX",
        ["year"] = 2020,
        ["grossWeightKg"] = 8000,
        ["requiredLicenceClass"] = "C",
        ["ownership"] = ownership,
        ["leaseEndDate"] = leaseEndDate,
        ["trailerType"] = trailerType,
        ["odometerKm"] = odometerKm,
        ["depotId"] = (Guid?)null,
        ["serviceIntervalKm"] = kind == 1 ? null : 50000,
        ["serviceIntervalMonths"] = kind == 1 ? null : 12,
        ["lastTachoDownloadAt"] = (string?)null,
        ["reason"] = reason,
    };

    private Task<HttpResponseMessage> PostBody(HttpClient client, Dictionary<string, object?> body) =>
        client.PostAsJsonAsync(VehiclesPath, body, TestContext.Current.CancellationToken);

    private Task<HttpResponseMessage> Post(
        HttpClient client,
        string? plate = null,
        string? vin = null,
        string make = "MAN",
        int kind = 0,
        int? trailerType = null,
        int? odometerKm = 1000,
        int ownership = 0,
        string? leaseEndDate = null,
        string? reason = "reason") =>
        PostBody(client, ValidCreateBody(plate, vin, make, kind, trailerType, odometerKm, ownership, leaseEndDate, reason));

    private async Task<VehicleDetail> CreateTrailer(HttpClient client)
    {
        using var response = await Post(client, kind: 1, trailerType: 1, odometerKm: null);
        response.EnsureSuccessStatusCode();
        return await ReadDetail(response);
    }

    /// <summary>The full edit payload for a vehicle exactly as the API returned it (read-then-write).</summary>
    private static Dictionary<string, object?> EditBody(VehicleDetail vehicle, int? version = null, string? reason = "edit reason") => new()
    {
        ["version"] = version ?? vehicle.Version,
        ["kind"] = (int)vehicle.Kind,
        ["plate"] = vehicle.Plate,
        ["vin"] = vehicle.Vin,
        ["make"] = vehicle.Make,
        ["model"] = vehicle.Model,
        ["year"] = vehicle.Year,
        ["grossWeightKg"] = vehicle.GrossWeightKg,
        ["requiredLicenceClass"] = vehicle.RequiredLicenceClass,
        ["ownership"] = (int)vehicle.Ownership,
        ["leaseEndDate"] = vehicle.LeaseEndDate,
        ["trailerType"] = (int?)vehicle.TrailerType,
        ["odometerKm"] = vehicle.OdometerKm,
        ["depotId"] = vehicle.DepotId,
        ["serviceIntervalKm"] = vehicle.ServiceIntervalKm,
        ["serviceIntervalMonths"] = vehicle.ServiceIntervalMonths,
        ["lastTachoDownloadAt"] = vehicle.LastTachoDownloadAt,
        ["reason"] = reason,
    };

    private Task<HttpResponseMessage> PutBody(HttpClient client, Guid id, Dictionary<string, object?> body) =>
        client.PutAsJsonAsync($"{VehiclesPath}/{id}", body, TestContext.Current.CancellationToken);

    private Task<HttpResponseMessage> Put(
        HttpClient client,
        VehicleDetail vehicle,
        string? plate = null,
        string? make = null,
        string? model = null,
        int? version = null,
        string? reason = "edit reason")
    {
        var body = EditBody(vehicle, version, reason);
        body["plate"] = plate ?? vehicle.Plate;
        body["make"] = make ?? vehicle.Make;
        body["model"] = model ?? vehicle.Model;
        return PutBody(client, vehicle.Id, body);
    }

    private async Task<VehicleDetail> Edit(
        HttpClient client,
        VehicleDetail vehicle,
        string? plate = null,
        string? make = null,
        string? model = null,
        int? version = null,
        string reason = "edit reason")
    {
        using var response = await Put(client, vehicle, plate, make, model, version, reason);
        response.EnsureSuccessStatusCode();
        return await ReadDetail(response);
    }

    private async Task<VehicleDetail> Get(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"{VehiclesPath}/{id}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadDetail(response);
    }

    private static async Task<VehicleDetail> ReadDetail(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<VehicleDetail>(cancellationToken: TestContext.Current.CancellationToken))!;

    // ------------------------------------------------------------------ helpers: reading responses

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The field names listed in a 400 response.</summary>
    private static async Task<string[]> Fields(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.TryGetProperty("fields", out var fields)
            ? fields.EnumerateArray().Select(field => field.GetString()!).ToArray()
            : [];
    }

    // ------------------------------------------------------------------ helpers: database

    private sealed record AuditRow(string Field, string? OldValue, string? NewValue, string ActorId, string Reason);

    private async Task<List<AuditRow>> AuditRows(Guid entityId, string? reason = null)
    {
        await using var connection = await factory.DirectConnection();
        await using var query = new NpgsqlCommand(
            "select field_name, old_value, new_value, actor_id, reason from fleetops.audit_entries " +
            "where entity_id = @id and (@reason::text is null or reason = @reason) order by field_name, created_at",
            connection);
        query.Parameters.AddWithValue("id", entityId);
        query.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        await using var reader = await query.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<AuditRow>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(new AuditRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return rows;
    }

    private async Task Retire(Guid vehicleId)
    {
        await using var connection = await factory.DirectConnection();
        await using var retire = new NpgsqlCommand("update fleetops.vehicles set status = 'Retired' where id = @id", connection);
        retire.Parameters.AddWithValue("id", vehicleId);
        Assert.Equal(1, await retire.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    private static Vehicle NewVehicle(Guid depotId) => new()
    {
        Kind = VehicleKind.Truck,
        Plate = "B-Z 0001",
        Vin = "WVWZZZ1JZXW000001",
        Make = "MAN",
        Model = "TGX",
        Year = 2020,
        GrossWeightKg = 8000,
        Ownership = VehicleOwnership.Owned,
        OdometerKm = 1,
        DepotId = depotId,
    };

    private SpecialFixtureContext NewFixtureContext(string reason)
    {
        var options = new DbContextOptionsBuilder<SpecialFixtureContext>()
            .UseNpgsql(factory.RuntimeConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor())
            .Options;
        return new SpecialFixtureContext(options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = reason };
    }

    /// <summary>Test-only audited entity: the real model has no field classed as special yet.</summary>
    [AuditedEntity]
    private sealed class SpecialFixtureRow
    {
        [NotAudited]
        public Guid Id { get; set; }

        [AuditField(AuditFieldClasses.Plain)]
        public string Label { get; set; } = "";

        [AuditField(AuditFieldClasses.Special)]
        public string Secret { get; set; } = "";
    }

    private sealed class SpecialFixtureContext(DbContextOptions<SpecialFixtureContext> options) : DbContext(options), IAuditDbContext
    {
        public DbSet<SpecialFixtureRow> Rows => Set<SpecialFixtureRow>();

        public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

        public Guid AuditActorId { get; set; }

        public string AuditReason { get; set; } = "";

        public void AddAudit(AuditEntry entry) => AuditEntries.Add(entry);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("fleetops");
            modelBuilder.Entity<SpecialFixtureRow>(entity =>
            {
                entity.ToTable("special_fixture_rows");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).HasColumnName("id");
                entity.Property(row => row.Label).HasColumnName("label");
                entity.Property(row => row.Secret).HasColumnName("secret");
            });
            modelBuilder.Entity<AuditEntry>(entity =>
            {
                entity.ToTable("audit_entries");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).HasColumnName("id");
                entity.Property(row => row.EntityType).HasColumnName("entity_type");
                entity.Property(row => row.EntityId).HasColumnName("entity_id");
                entity.Property(row => row.FieldName).HasColumnName("field_name");
                entity.Property(row => row.OldValue).HasColumnName("old_value");
                entity.Property(row => row.NewValue).HasColumnName("new_value");
                entity.Property(row => row.ActorId).HasColumnName("actor_id");
                entity.Property(row => row.Reason).HasColumnName("reason");
                entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            });
        }
    }
}
