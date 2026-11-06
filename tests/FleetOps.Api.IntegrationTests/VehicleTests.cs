using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

public sealed partial class VehicleTests : IAsyncLifetime
{
    private const string VehiclesPath = "/api/v1/fleet/vehicles";

    private readonly VehicleApiFactory factory = new();

    public ValueTask InitializeAsync() => factory.InitializeAsync();

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    // ------------------------------------------------------------------ create

    [Fact]
    public async Task Dispatcher_adds_a_truck_and_required_documents_are_listed()
    {
        using var client = factory.AsDispatcher();

        using var response = await Post(client);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var vehicle = await ReadDetail(response);
        Assert.Equal(VehicleStatus.Active, vehicle.Status);
        Assert.Equal(
            ["registration", "HU", "SP", "tacho_calibration", "insurance", "community_licence"],
            vehicle.RequiredDocuments.Select(document => document.Type).ToArray());
        Assert.Equal(6, vehicle.RequiredDocuments.Count);
    }

    [Fact]
    public async Task Owner_adds_a_truck()
    {
        using var client = factory.AsOwner();

        var vehicle = await factory.CreateTruck(client);

        Assert.Equal(VehicleKind.Truck, vehicle.Kind);
    }

    [Fact]
    public async Task Trailer_lists_trailer_documents_and_flags_sp()
    {
        using var client = factory.AsOwner();

        var trailer = await CreateTrailer(client);

        Assert.Contains(trailer.RequiredDocuments, document => document.Type == "SP" && document.NeedsConfirmation);
    }

    [Fact]
    public async Task Truck_at_or_under_7500_kg_does_not_list_sp()
    {
        using var client = factory.AsOwner();

        var truck = await factory.CreateTruck(client, grossWeightKg: 7500);

        Assert.DoesNotContain(truck.RequiredDocuments, document => document.Type == "SP");
    }

    [Fact]
    public async Task Truck_at_7501_kg_lists_sp()
    {
        using var client = factory.AsOwner();

        var truck = await factory.CreateTruck(client, grossWeightKg: 7501);

        Assert.Contains(truck.RequiredDocuments, document => document.Type == "SP" && !document.NeedsConfirmation);
        var atLimit = await factory.CreateTruck(client, grossWeightKg: 7500);
        Assert.DoesNotContain(atLimit.RequiredDocuments, document => document.Type == "SP");
    }

    [Fact]
    public async Task Hr_cannot_add_a_vehicle_and_nothing_is_written()
    {
        using var client = factory.AsHr();
        var before = await factory.CountRows("vehicles");

        using var response = await Post(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("permission_denied", await Code(response));
        Assert.Equal(before, await factory.CountRows("vehicles"));
    }

    [Fact]
    public async Task Driver_cannot_add_a_vehicle_and_nothing_is_written()
    {
        using var client = factory.AsDriver();
        var before = await factory.CountRows("vehicles");

        using var response = await Post(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("permission_denied", await Code(response));
        Assert.Equal(before, await factory.CountRows("vehicles"));
    }

    [Fact]
    public async Task Invalid_plate_lists_the_field_name_and_not_the_value()
    {
        using var client = factory.AsOwner();

        using var response = await Post(client, plate: "bad");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("plate", body);
        Assert.DoesNotContain("bad", body);
    }

    [Fact]
    public async Task Invalid_vin_is_refused()
    {
        using var client = factory.AsOwner();

        using var response = await Post(client, vin: "I".PadRight(17, '1'));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("vin", await Fields(response));
    }

    [Fact]
    public async Task Missing_required_field_is_refused()
    {
        using var client = factory.AsOwner();

        using var response = await Post(client, make: "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("make", await Fields(response));
    }

    [Fact]
    public async Task Missing_plate_returns_400()
    {
        using var client = factory.AsOwner();
        var before = await factory.CountRows("vehicles");
        var withoutPlate = ValidCreateBody();
        withoutPlate.Remove("plate");
        var nullPlate = ValidCreateBody();
        nullPlate["plate"] = null;

        using var omitted = await PostBody(client, withoutPlate);
        using var explicitNull = await PostBody(client, nullPlate);

        Assert.Equal(HttpStatusCode.BadRequest, omitted.StatusCode);
        Assert.Contains("plate", await Fields(omitted));
        Assert.Equal(HttpStatusCode.BadRequest, explicitNull.StatusCode);
        Assert.Contains("plate", await Fields(explicitNull));
        Assert.Equal(before, await factory.CountRows("vehicles"));
    }

    [Fact]
    public async Task Duplicate_plate_is_refused()
    {
        using var client = factory.AsOwner();
        var existing = await factory.CreateTruck(client);

        using var response = await Post(client, plate: existing.Plate);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("duplicate_vehicle", await Code(response));
    }

    [Fact]
    public async Task Duplicate_vin_is_refused()
    {
        using var client = factory.AsOwner();
        var existing = await factory.CreateTruck(client);

        using var response = await Post(client, vin: existing.Vin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("duplicate_vehicle", await Code(response));
    }

    [Fact]
    public async Task Concurrent_duplicate_plates_return_409_not_500()
    {
        using var client = factory.AsOwner();
        var sharedPlate = factory.NextPlate();
        const int parallelRequests = 8;

        var responses = await Task.WhenAll(Enumerable.Range(0, parallelRequests).Select(_ => Post(client, plate: sharedPlate)));

        var statuses = responses.Select(response => response.StatusCode).ToArray();
        Assert.All(statuses, status => Assert.Contains(status, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, statuses);
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
        foreach (var conflict in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Equal("duplicate_vehicle", await Code(conflict));
        }

        await using var connection = await factory.DirectConnection();
        await using var count = new NpgsqlCommand("select count(*) from fleetops.vehicles where plate = @plate", connection);
        count.Parameters.AddWithValue("plate", sharedPlate);
        Assert.Equal(1L, await count.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Leased_vehicle_returns_ownership_and_end_date()
    {
        using var client = factory.AsOwner();

        using var response = await Post(client, ownership: 1, leaseEndDate: "2030-01-01");

        var vehicle = await ReadDetail(response);
        Assert.Equal(VehicleOwnership.Leased, vehicle.Ownership);
        Assert.Equal(new DateOnly(2030, 1, 1), vehicle.LeaseEndDate);
    }

    [Fact]
    public async Task Lease_end_date_is_refused_for_owned_vehicles()
    {
        using var client = factory.AsOwner();

        using var response = await Post(client, leaseEndDate: "2030-01-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("leaseEndDate", await Fields(response));
    }

    [Fact]
    public async Task Trailer_type_is_refused_on_a_truck()
    {
        using var client = factory.AsOwner();

        using var response = await Post(client, trailerType: 1);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("trailerType", await Fields(response));
    }

    [Fact]
    public async Task Odometer_is_refused_on_a_trailer()
    {
        using var client = factory.AsOwner();

        using var response = await Post(client, kind: 1, trailerType: 1, odometerKm: 1);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("odometerKm", await Fields(response));
    }

    [Fact]
    public async Task Service_km_is_refused_on_a_trailer()
    {
        using var client = factory.AsOwner();
        var before = await factory.CountRows("vehicles");
        var trailer = ValidCreateBody(kind: 1, trailerType: 1, odometerKm: null);
        trailer["serviceIntervalKm"] = 50000;
        trailer["serviceIntervalMonths"] = null;

        using var response = await PostBody(client, trailer);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = await Fields(response);
        Assert.Contains("serviceIntervalKm", fields);
        Assert.DoesNotContain("odometerKm", fields);
        Assert.Equal(before, await factory.CountRows("vehicles"));
    }

    [Fact]
    public async Task Trailer_without_type_is_refused()
    {
        using var client = factory.AsOwner();

        using var response = await Post(client, kind: 1, trailerType: null, odometerKm: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("trailerType", await Fields(response));
    }

    [Fact]
    public async Task Unknown_enum_is_refused()
    {
        using var client = factory.AsOwner();
        var before = await factory.CountRows("vehicles");

        using var unknownKind = await Post(client, kind: 99);
        using var unknownOwnership = await Post(client, ownership: 99);
        using var unknownTrailerType = await Post(client, kind: 1, trailerType: 99, odometerKm: null);

        Assert.Equal(HttpStatusCode.BadRequest, unknownKind.StatusCode);
        Assert.Contains("kind", await Fields(unknownKind));
        Assert.Equal(HttpStatusCode.BadRequest, unknownOwnership.StatusCode);
        Assert.Contains("ownership", await Fields(unknownOwnership));
        Assert.Equal(HttpStatusCode.BadRequest, unknownTrailerType.StatusCode);
        Assert.Contains("trailerType", await Fields(unknownTrailerType));
        Assert.Equal(before, await factory.CountRows("vehicles"));
    }

    [Fact]
    public async Task Negative_odometer_is_refused()
    {
        using var client = factory.AsOwner();
        var existing = await factory.CreateTruck(client);

        using var onCreate = await Post(client, odometerKm: -1);
        var negativeEdit = EditBody(existing);
        negativeEdit["odometerKm"] = -1;
        using var onEdit = await PutBody(client, existing.Id, negativeEdit);

        Assert.Equal(HttpStatusCode.BadRequest, onCreate.StatusCode);
        Assert.Contains("odometerKm", await Fields(onCreate));
        Assert.Equal(HttpStatusCode.BadRequest, onEdit.StatusCode);
        Assert.Contains("odometerKm", await Fields(onEdit));
        Assert.Equal(1000, (await Get(client, existing.Id)).OdometerKm);
    }

    [Fact]
    public async Task Plate_with_trailing_newline_is_refused()
    {
        using var client = factory.AsOwner();
        var before = await factory.CountRows("vehicles");
        var plate = factory.NextPlate();

        using var plateResponse = await Post(client, plate: plate + "\n");
        using var vinResponse = await Post(client, vin: factory.NextVin() + "\n");

        Assert.Equal(HttpStatusCode.BadRequest, plateResponse.StatusCode);
        Assert.Contains("plate", await Fields(plateResponse));
        Assert.Equal(HttpStatusCode.BadRequest, vinResponse.StatusCode);
        Assert.Contains("vin", await Fields(vinResponse));
        Assert.Equal(before, await factory.CountRows("vehicles"));
    }

    [Fact]
    public async Task Missing_reason_is_refused_on_create()
    {
        using var client = factory.AsOwner();
        var before = await factory.CountRows("vehicles");
        var omitted = ValidCreateBody();
        omitted.Remove("reason");

        using var withoutReason = await PostBody(client, omitted);
        using var nullReason = await Post(client, reason: null);
        using var emptyReason = await Post(client, reason: "");
        using var blankReason = await Post(client, reason: "   ");

        foreach (var response in new[] { withoutReason, nullReason, emptyReason, blankReason })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("reason", await Fields(response));
        }

        Assert.Equal(before, await factory.CountRows("vehicles"));
    }

    [Fact]
    public async Task Unknown_depot_is_refused()
    {
        using var client = factory.AsOwner();
        var existing = await factory.CreateTruck(client);
        var before = await factory.CountRows("vehicles");
        var unknownDepot = Guid.CreateVersion7();
        var createBody = ValidCreateBody();
        createBody["depotId"] = unknownDepot;
        var editBody = EditBody(existing);
        editBody["depotId"] = unknownDepot;

        using var onCreate = await PostBody(client, createBody);
        using var onEdit = await PutBody(client, existing.Id, editBody);

        Assert.Equal(HttpStatusCode.BadRequest, onCreate.StatusCode);
        Assert.Contains("depotId", await Fields(onCreate));
        Assert.Equal(HttpStatusCode.BadRequest, onEdit.StatusCode);
        Assert.Contains("depotId", await Fields(onEdit));
        Assert.Equal(before, await factory.CountRows("vehicles"));
        Assert.Equal(existing.DepotId, (await Get(client, existing.Id)).DepotId);
    }

    [Fact]
    public async Task Default_depot_is_used()
    {
        using var client = factory.AsOwner();

        var vehicle = await factory.CreateTruck(client);

        Assert.NotEqual(Guid.Empty, vehicle.DepotId);
        await using var connection = await factory.DirectConnection();
        await using var depot = new NpgsqlCommand("select id from fleetops.depots", connection);
        Assert.Equal(vehicle.DepotId, (Guid)(await depot.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task Exactly_one_depot_exists_after_parallel_creates()
    {
        using var client = factory.AsOwner();
        Assert.Equal(1L, await factory.CountRows("depots"));
        const int parallelRequests = 8;

        var responses = await Task.WhenAll(Enumerable.Range(0, parallelRequests).Select(_ => Post(client)));

        var statuses = responses.Select(response => response.StatusCode).ToArray();
        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Created, status));
        Assert.Equal(1L, await factory.CountRows("depots"));
        var depotIds = new List<Guid>();
        foreach (var response in responses)
        {
            depotIds.Add((await ReadDetail(response)).DepotId);
            response.Dispose();
        }

        Assert.Single(depotIds.Distinct());
        Assert.Equal(parallelRequests, await factory.CountRows("vehicles"));
    }

    // ------------------------------------------------------------------ edit

    [Fact]
    public async Task Plate_change_keeps_the_old_plate_in_history()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);

        var changed = await Edit(client, vehicle, plate: "B-H 9998", reason: "plate history reason");

        var current = await Get(client, vehicle.Id);
        Assert.Equal(changed.Plate, current.Plate);
        Assert.Equal("B-H 9998", current.Plate);
        Assert.Contains(vehicle.Plate, current.PlateHistory);
        var plateAudit = Assert.Single(await AuditRows(vehicle.Id, "plate history reason"), row => row.Field == "Plate");
        Assert.Equal(vehicle.Plate, plateAudit.OldValue);
        Assert.Equal("B-H 9998", plateAudit.NewValue);
    }

    [Fact]
    public async Task Plate_change_requires_a_reason()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);

        using var response = await Put(client, vehicle, plate: "B-H 9997", reason: "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("reason", await Fields(response));
    }

    [Fact]
    public async Task Blank_reason_is_refused_on_edit()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        var auditBefore = await factory.CountRows("audit_entries");

        using var emptyReason = await Put(client, vehicle, make: "DAF", reason: "");
        using var whitespaceReason = await Put(client, vehicle, make: "DAF", reason: "   ");
        var omitted = EditBody(vehicle);
        omitted["make"] = "DAF";
        omitted.Remove("reason");
        using var withoutReason = await PutBody(client, vehicle.Id, omitted);

        foreach (var response in new[] { emptyReason, whitespaceReason, withoutReason })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("reason", await Fields(response));
        }

        var unchanged = await Get(client, vehicle.Id);
        Assert.Equal("MAN", unchanged.Make);
        Assert.Equal(vehicle.Version, unchanged.Version);
        Assert.Equal(auditBefore, await factory.CountRows("audit_entries"));
    }

    [Fact]
    public async Task Stale_version_is_refused_and_nothing_is_overwritten()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        var current = await Edit(client, vehicle, make: "DAF");

        using var response = await Put(client, current, version: vehicle.Version, make: "BROKEN");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("version_conflict", await Code(response));
        var stored = await Get(client, vehicle.Id);
        Assert.Equal("DAF", stored.Make);
        Assert.Equal(current.Version, stored.Version);
    }

    [Fact]
    public async Task Future_version_is_refused()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);

        using var response = await Put(client, vehicle, version: vehicle.Version + 1, make: "BROKEN");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("version_conflict", await Code(response));
        var stored = await Get(client, vehicle.Id);
        Assert.Equal("MAN", stored.Make);
        Assert.Equal(vehicle.Version, stored.Version);
    }

    [Fact]
    public async Task Stale_version_writes_no_audit_entry()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        var before = await factory.CountRows("audit_entries");

        using var response = await Put(client, vehicle, version: vehicle.Version - 1, make: "BROKEN");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await factory.CountRows("audit_entries"));
    }

    [Fact]
    public async Task Version_increases_on_each_edit()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);

        var one = await Edit(client, vehicle, make: "DAF");
        var two = await Edit(client, one, make: "IVECO");

        Assert.True(one.Version > vehicle.Version);
        Assert.True(two.Version > one.Version);
        Assert.Equal(two.Version, (await Get(client, vehicle.Id)).Version);
    }

    [Fact]
    public async Task Read_then_write_keeps_every_field()
    {
        using var client = factory.AsOwner();
        var createBody = ValidCreateBody(ownership: 1, leaseEndDate: "2031-05-17");
        createBody["year"] = 2019;
        createBody["grossWeightKg"] = 12000;
        createBody["requiredLicenceClass"] = "CE";
        createBody["odometerKm"] = 123456;
        createBody["serviceIntervalKm"] = 60000;
        createBody["serviceIntervalMonths"] = 6;
        createBody["lastTachoDownloadAt"] = "2026-10-01";
        using var created = await PostBody(client, createBody);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var vehicle = await ReadDetail(created);
        var read = await Get(client, vehicle.Id);

        using var written = await PutBody(client, read.Id, EditBody(read, reason: "round trip"));

        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
        var afterWrite = await ReadDetail(written);
        var stored = await Get(client, read.Id);
        foreach (var result in new[] { afterWrite, stored })
        {
            Assert.Equal(read.Kind, result.Kind);
            Assert.Equal(read.Plate, result.Plate);
            Assert.Equal(read.Vin, result.Vin);
            Assert.Equal(read.Make, result.Make);
            Assert.Equal(read.Model, result.Model);
            Assert.Equal(2019, result.Year);
            Assert.Equal(12000, result.GrossWeightKg);
            Assert.Equal("CE", result.RequiredLicenceClass);
            Assert.Equal(VehicleOwnership.Leased, result.Ownership);
            Assert.Equal(new DateOnly(2031, 5, 17), result.LeaseEndDate);
            Assert.Equal(123456, result.OdometerKm);
            Assert.Equal(60000, result.ServiceIntervalKm);
            Assert.Equal(6, result.ServiceIntervalMonths);
            Assert.Equal(new DateOnly(2026, 10, 1), result.LastTachoDownloadAt);
            Assert.Equal(read.DepotId, result.DepotId);
            Assert.Equal(read.Status, result.Status);
        }
    }

    [Fact]
    public async Task Concurrent_edits_return_409_not_500()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        const int parallelRequests = 8;

        var responses = await Task.WhenAll(Enumerable.Range(0, parallelRequests)
            .Select(index => Put(client, vehicle, make: $"MAKE{index}", reason: $"parallel edit {index}")));

        var statuses = responses.Select(response => response.StatusCode).ToArray();
        Assert.All(statuses, status => Assert.Contains(status, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, statuses);
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
        foreach (var conflict in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Equal("version_conflict", await Code(conflict));
        }

        var stored = await Get(client, vehicle.Id);
        Assert.Equal(vehicle.Version + 1, stored.Version);
        var winner = await ReadDetail(responses.Single(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(winner.Make, stored.Make);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Kind_cannot_change_on_edit()
    {
        using var client = factory.AsOwner();
        var truck = await factory.CreateTruck(client);
        var asTrailer = EditBody(truck);
        asTrailer["kind"] = (int)VehicleKind.Trailer;
        asTrailer["trailerType"] = 1;
        asTrailer["odometerKm"] = null;
        asTrailer["serviceIntervalKm"] = null;
        asTrailer["serviceIntervalMonths"] = null;

        using var response = await PutBody(client, truck.Id, asTrailer);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("kind", await Fields(response));
        var stored = await Get(client, truck.Id);
        Assert.Equal(VehicleKind.Truck, stored.Kind);
        Assert.Null(stored.TrailerType);
        Assert.Equal(truck.Version, stored.Version);
    }

    [Fact]
    public async Task Retired_vehicle_cannot_be_edited()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        await Retire(vehicle.Id);
        var auditBefore = await factory.CountRows("audit_entries");

        using var response = await Put(client, vehicle, make: "DAF");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("state_conflict", await Code(response));
        var stored = await Get(client, vehicle.Id);
        Assert.Equal("MAN", stored.Make);
        Assert.Equal(VehicleStatus.Retired, stored.Status);
        Assert.Equal(auditBefore, await factory.CountRows("audit_entries"));
    }

    [Fact]
    public async Task Hr_and_driver_cannot_edit()
    {
        using var owner = factory.AsOwner();
        var vehicle = await factory.CreateTruck(owner);
        var auditBefore = await factory.CountRows("audit_entries");

        using var hr = factory.AsHr();
        using var hrResponse = await Put(hr, vehicle, make: "BROKEN");
        using var driver = factory.AsDriver();
        using var driverResponse = await Put(driver, vehicle, make: "BROKEN");

        Assert.Equal(HttpStatusCode.Forbidden, hrResponse.StatusCode);
        Assert.Equal("permission_denied", await Code(hrResponse));
        Assert.Equal(HttpStatusCode.Forbidden, driverResponse.StatusCode);
        Assert.Equal("permission_denied", await Code(driverResponse));
        using var ownerAgain = factory.AsOwner();
        var stored = await Get(ownerAgain, vehicle.Id);
        Assert.Equal("MAN", stored.Make);
        Assert.Equal(vehicle.Version, stored.Version);
        Assert.Equal(auditBefore, await factory.CountRows("audit_entries"));
    }

    [Fact]
    public async Task Edit_to_a_taken_plate_is_refused()
    {
        using var client = factory.AsOwner();
        var first = await factory.CreateTruck(client);
        var second = await factory.CreateTruck(client);
        var auditBefore = await factory.CountRows("audit_entries");
        var historyBefore = await factory.CountRows("vehicle_plate_history");

        using var response = await Put(client, second, plate: first.Plate, reason: "take the other plate");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("duplicate_vehicle", await Code(response));
        var stored = await Get(client, second.Id);
        Assert.Equal(second.Plate, stored.Plate);
        Assert.Equal(second.Version, stored.Version);
        Assert.Equal(auditBefore, await factory.CountRows("audit_entries"));
        Assert.Equal(historyBefore, await factory.CountRows("vehicle_plate_history"));
    }
}
