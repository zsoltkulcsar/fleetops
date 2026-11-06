using System.Text.Json;
using FleetOps.Migrations;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Readiness;
using FleetOps.Modules.Fleet;
using FleetOps.Kernel;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FleetOps.Tests;

internal static class ReadinessTestSupport
{
    public static async Task AddEmployee(VehicleApiFactory factory, Guid id, string roles = "[\"driver\"]", EmployeeStatus status = EmployeeStatus.Active)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into fleetops.employees (id, first_name, last_name, email, roles, status, start_date) values (@id, 'Test', 'Driver', @email, @roles, @status, date '2026-01-01')", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("email", $"{id}@test");
        command.Parameters.AddWithValue("roles", roles);
        command.Parameters.AddWithValue("status", status.ToString());
        await command.ExecuteNonQueryAsync();
    }

    public static async Task AddDocument(VehicleApiFactory factory, DocumentOwnerType ownerType, Guid ownerId, DocumentType type, DateOnly? expiresAt, DocumentStatus status = DocumentStatus.Verified, int version = 1)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into fleetops.documents (id, owner_type, owner_id, type, expires_at, status, categories, import_attested, created_by, created_at, version) values (@id, @owner_type, @owner_id, @type, @expires_at, @status, '{}', false, @created_by, now(), @version)", connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("owner_type", ownerType.ToString());
        command.Parameters.AddWithValue("owner_id", ownerId);
        command.Parameters.AddWithValue("type", TypeName(type));
        command.Parameters.AddWithValue("expires_at", (object?)expiresAt ?? DBNull.Value);
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("created_by", VehicleApiFactory.ActorId);
        command.Parameters.AddWithValue("version", version);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task SetEmployee(VehicleApiFactory factory, Guid id, EmployeeStatus status, string roles = "[\"driver\"]")
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("update fleetops.employees set status = @status, roles = @roles where id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("roles", roles);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task SetVehicle(VehicleApiFactory factory, Guid id, VehicleStatus status, bool manualOffRoad = false)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("update fleetops.vehicles set status = @status, manual_off_road = @manual_off_road where id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("manual_off_road", manualOffRoad);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<VehicleReadiness> Vehicle(VehicleApiFactory factory, Guid id, DateOnly? date = null)
    {
        using var scope = factory.Services.CreateScope();
        var readiness = scope.ServiceProvider.GetRequiredService<FleetOps.Modules.Readiness.IReadiness>();
        return (await readiness.ForVehiclesAsync([id], date ?? new DateOnly(2026, 11, 6), CancellationToken.None))[id];
    }

    public static async Task<DriverReadiness> Driver(VehicleApiFactory factory, Guid id, DateOnly? date = null)
    {
        using var scope = factory.Services.CreateScope();
        var readiness = scope.ServiceProvider.GetRequiredService<FleetOps.Modules.Readiness.IReadiness>();
        return (await readiness.ForDriversAsync([id], date ?? new DateOnly(2026, 11, 6), CancellationToken.None))[id];
    }

    public static async Task AddAllVehicleDocuments(VehicleApiFactory factory, Guid vehicleId, DateOnly expiry = default)
    {
        expiry = expiry == default ? new DateOnly(2027, 1, 1) : expiry;
        foreach (var type in new[] { DocumentType.Registration, DocumentType.HuReport, DocumentType.SpReport, DocumentType.TachoCalibration, DocumentType.Insurance, DocumentType.CommunityLicence })
            await AddDocument(factory, DocumentOwnerType.Vehicle, vehicleId, type, type == DocumentType.Registration ? null : expiry);
    }

    public static async Task AddAllDriverDocuments(VehicleApiFactory factory, Guid employeeId, DateOnly expiry = default)
    {
        expiry = expiry == default ? new DateOnly(2027, 1, 1) : expiry;
        foreach (var type in new[] { DocumentType.LicenceC_CE, DocumentType.DriverCard, DocumentType.Code95, DocumentType.Medical })
            await AddDocument(factory, DocumentOwnerType.Employee, employeeId, type, expiry);
    }

    private static string TypeName(DocumentType type) => type switch
    {
        DocumentType.LicenceC_CE => "licence_c_ce",
        DocumentType.DriverCard => "driver_card",
        DocumentType.Code95 => "code95",
        DocumentType.AdrCertificate => "adr_certificate",
        DocumentType.HuReport => "hu_report",
        DocumentType.SpReport => "sp_report",
        DocumentType.TachoCalibration => "tacho_calibration",
        DocumentType.CommunityLicence => "community_licence",
        _ => type.ToString().ToLowerInvariant(),
    };
}
