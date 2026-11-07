using FleetOps.Modules.Documents;
using FleetOps.Modules.Fleet;
using Xunit;

namespace FleetOps.Tests;

public sealed class AlertRuleTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();

    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task A02_opens_when_a_blocking_document_is_missing()
    {
        var employee = await AlertTestSupport.AddEmployee(factory);
        await AlertTestSupport.Apply(factory, "employee", employee);
        var alert = await AlertTestSupport.Alert(factory, "A-02", "employee", employee);
        Assert.Equal(("Open", "Critical", "hr", "licence_c_ce"), (alert.Status, alert.Severity, alert.Role, alert.Scope));
    }

    [Fact]
    public async Task A02_opens_when_a_blocking_document_expires()
    {
        var employee = await AlertTestSupport.AddEmployee(factory);
        await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, employee, DocumentType.Code95, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(-1));
        await AlertTestSupport.Apply(factory, "employee", employee);
        Assert.Equal("Open", (await AlertTestSupport.Alert(factory, "A-02", "employee", employee, "code95")).Status);
    }

    [Fact]
    public async Task A02_resolves_when_a_valid_document_is_verified()
    {
        var employee = await AlertTestSupport.AddEmployee(factory);
        var document = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, employee, DocumentType.Code95, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(-1));
        await AlertTestSupport.Apply(factory, "employee", employee);
        await AlertTestSupport.SetDocumentStatus(factory, document, "Verified");
        await using var connection = new Npgsql.NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("update fleetops.documents set expires_at = date '2027-01-01' where id = $1", connection);
        command.Parameters.AddWithValue(document);
        await command.ExecuteNonQueryAsync();
        await AlertTestSupport.Apply(factory, "employee", employee);
        var alert = await AlertTestSupport.Alert(factory, "A-02", "employee", employee, "code95");
        Assert.Equal("Resolved", alert.Status);
        Assert.NotNull(alert.OpenedAt);
    }

    [Fact]
    public async Task A02_stays_out_of_non_drivers()
    {
        var employee = await AlertTestSupport.AddEmployee(factory, "[\"hr\"]");
        await AlertTestSupport.Apply(factory, "employee", employee);
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-02", employee));
    }

    [Fact]
    public async Task A02_has_one_alert_per_document_type()
    {
        var employee = await AlertTestSupport.AddEmployee(factory);
        await AlertTestSupport.Apply(factory, "employee", employee);
        await AlertTestSupport.Apply(factory, "employee", employee);
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-02", employee, "Open", "licence_c_ce"));
    }

    [Fact]
    public async Task A02_clears_when_the_person_is_suspended()
    {
        var employee = await AlertTestSupport.AddEmployee(factory);
        await AlertTestSupport.Apply(factory, "employee", employee);
        await ReadinessTestSupport.SetEmployee(factory, employee, FleetOps.People.EmployeeStatus.Suspended);
        await AlertTestSupport.Apply(factory, "employee", employee);
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-02", employee));
    }

    [Fact]
    public async Task A03_opens_after_the_stored_queue_days()
    {
        var document = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, Guid.NewGuid(), DocumentType.Code95, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(30), "Uploaded", factory.Clock.GetUtcNow().AddDays(-3));
        await AlertTestSupport.Apply(factory, "document", document);
        Assert.Equal("Open", (await AlertTestSupport.Alert(factory, "A-03", "document", document)).Status);
    }

    [Fact]
    public async Task A03_opens_after_an_hour_when_the_upload_blocks_a_driver()
    {
        var document = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, Guid.NewGuid(), DocumentType.Code95, null, "Uploaded", factory.Clock.GetUtcNow().AddHours(-2));
        await AlertTestSupport.Apply(factory, "document", document);
        Assert.Equal("Open", (await AlertTestSupport.Alert(factory, "A-03", "document", document)).Status);
    }

    [Fact]
    public async Task A03_uses_the_stored_setting_not_a_constant()
    {
        await AlertTestSupport.SetSetting(factory, "alerts.a03.queue_days", 5);
        var document = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, Guid.NewGuid(), DocumentType.AdrCertificate, null, "Uploaded", factory.Clock.GetUtcNow().AddDays(-3));
        await AlertTestSupport.Apply(factory, "document", document);
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-03", document));
    }

    [Fact]
    public async Task A03_resolves_when_the_upload_is_verified()
    {
        var document = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, Guid.NewGuid(), DocumentType.Code95, null, "Uploaded", factory.Clock.GetUtcNow().AddDays(-3));
        await AlertTestSupport.Apply(factory, "document", document);
        await AlertTestSupport.SetDocumentStatus(factory, document, "Verified");
        await AlertTestSupport.Apply(factory, "document", document);
        Assert.Equal("Resolved", (await AlertTestSupport.Alert(factory, "A-03", "document", document)).Status);
    }

    [Fact]
    public async Task A03_resolves_when_the_upload_is_rejected()
    {
        var document = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, Guid.NewGuid(), DocumentType.Code95, null, "Uploaded", factory.Clock.GetUtcNow().AddDays(-3));
        await AlertTestSupport.Apply(factory, "document", document);
        await AlertTestSupport.SetDocumentStatus(factory, document, "Rejected");
        await AlertTestSupport.Apply(factory, "document", document);
        Assert.Equal("Resolved", (await AlertTestSupport.Alert(factory, "A-03", "document", document)).Status);
    }

    [Fact]
    public async Task A03_goes_to_dispatch_for_a_vehicle_document()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        var document = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, null, "Uploaded", factory.Clock.GetUtcNow().AddDays(-3));
        await AlertTestSupport.Apply(factory, "document", document);
        Assert.Equal("dispatcher", (await AlertTestSupport.Alert(factory, "A-03", "document", document)).Role);
    }

    [Fact]
    public async Task A05_opens_when_a_blocking_vehicle_document_expires()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(-1));
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        var alert = await AlertTestSupport.Alert(factory, "A-05", "vehicle", vehicle.Id, "hu_report");
        Assert.Equal(("Open", "Critical", "dispatcher", "hu_report"), (alert.Status, alert.Severity, alert.Role, alert.Scope));
    }

    [Fact]
    public async Task A05_resolves_when_a_valid_renewal_is_verified()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(-1));
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(30));
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        Assert.Equal("Resolved", (await AlertTestSupport.Alert(factory, "A-05", "vehicle", vehicle.Id, "hu_report")).Status);
    }

    [Fact]
    public async Task A05_stays_shut_for_a_missing_document()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-05", vehicle.Id));
    }

    [Fact]
    public async Task A05_resolves_when_the_vehicle_is_retired()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(-1));
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        await ReadinessTestSupport.SetVehicle(factory, vehicle.Id, VehicleStatus.Retired);
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        Assert.Equal("Resolved", (await AlertTestSupport.Alert(factory, "A-05", "vehicle", vehicle.Id, "hu_report")).Status);
    }

    [Fact]
    public async Task Snoozed_alert_stays_snoozed_while_its_condition_holds()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(-1));
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        await using var connection = new Npgsql.NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("update fleetops.alerts set status = 'Snoozed', snoozed_until = now() + interval '1 day' where rule_id = 'A-05' and subject_id = $1", connection);
        command.Parameters.AddWithValue(vehicle.Id);
        await command.ExecuteNonQueryAsync();
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        Assert.Equal("Snoozed", (await AlertTestSupport.Alert(factory, "A-05", "vehicle", vehicle.Id, "hu_report")).Status);
    }

    [Fact]
    public async Task Setting_default_applies_with_no_row()
    {
        var document = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, Guid.NewGuid(), DocumentType.Code95, null, "Uploaded", factory.Clock.GetUtcNow().AddDays(-3));
        await AlertTestSupport.Apply(factory, "document", document);
        Assert.Equal("Open", (await AlertTestSupport.Alert(factory, "A-03", "document", document)).Status);
    }
}

