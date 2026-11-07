using FleetOps.Api.Jobs;
using FleetOps.Migrations;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Fleet;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

internal static class AlertTestSupport
{
    public static async Task Apply(VehicleApiFactory factory, string subjectType, Guid subjectId)
    {
        await using var connection = new NpgsqlConnection(factory.RuntimeConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        using var scope = factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IAlertEngine>();
        await engine.ApplyAsync(subjectType, subjectId, new CommandContext(connection, transaction), CancellationToken.None);
        await transaction.CommitAsync();
    }

    public static async Task<(string Status, string Severity, string Role, string Scope, DateTimeOffset? OpenedAt, long Xmin)> Alert(VehicleApiFactory factory, string ruleId, string subjectType, Guid subjectId, string? scope = null)
    {
        await using var connection = new NpgsqlConnection(factory.RuntimeConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select status, severity, assigned_role, scope_key, opened_at, xmin::text::bigint from fleetops.alerts where rule_id = $1 and subject_type = $2 and subject_id = $3 and ($4::text is null or scope_key = $4) order by opened_at desc limit 1", connection);
        command.Parameters.AddWithValue(ruleId);
        command.Parameters.AddWithValue(subjectType);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(scope is null ? DBNull.Value : scope);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Missing alert {ruleId}/{subjectType}/{subjectId}");
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset?>(4), reader.GetInt64(5));
    }

    public static async Task<long> Count(VehicleApiFactory factory, string ruleId, Guid subjectId, string status = "Open", string? scope = null)
    {
        await using var connection = new NpgsqlConnection(factory.RuntimeConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select count(*) from fleetops.alerts where rule_id = $1 and subject_id = $2 and status = $3 and ($4::text is null or scope_key = $4)", connection);
        command.Parameters.AddWithValue(ruleId);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue(scope is null ? DBNull.Value : scope);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public static async Task SetSetting(VehicleApiFactory factory, string key, int value)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into fleetops.settings (key, value, updated_at, updated_by) values ($1, $2, now(), $3) on conflict (key) do update set value = excluded.value, updated_at = excluded.updated_at, updated_by = excluded.updated_by", connection);
        command.Parameters.AddWithValue(key);
        command.Parameters.AddWithValue(value);
        command.Parameters.AddWithValue("alert-tests");
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<Guid> AddEmployee(VehicleApiFactory factory, string roles = "[\"driver\"]", string status = "Active")
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into fleetops.employees (id, first_name, last_name, email, roles, status, start_date) values ($1, 'Alert', 'Test', $2, $3, $4, date '2026-01-01')", connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue($"{id}@alert.test");
        command.Parameters.AddWithValue(roles);
        command.Parameters.AddWithValue(status);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    public static async Task<Guid> AddDocument(VehicleApiFactory factory, DocumentOwnerType ownerType, Guid ownerId, DocumentType type, DateOnly? expiresAt, string status = "Verified", DateTimeOffset? createdAt = null)
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into fleetops.documents (id, owner_type, owner_id, type, expires_at, status, categories, import_attested, created_by, created_at, version) values ($1, $2, $3, $4, $5, $6, '{}', false, $7, $8, 1)", connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(ownerType.ToString());
        command.Parameters.AddWithValue(ownerId);
        command.Parameters.AddWithValue(TypeName(type));
        command.Parameters.AddWithValue((object?)expiresAt ?? DBNull.Value);
        command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue(VehicleApiFactory.ActorId);
        command.Parameters.AddWithValue(createdAt ?? factory.Clock.GetUtcNow());
        await command.ExecuteNonQueryAsync();
        return id;
    }

    public static async Task<JobStep> Sweep(VehicleApiFactory factory, string? cursor = null)
    {
        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetServices<IJob>().Single(x => x.Name == "alerts-sweep");
        return await job.RunStepAsync(new JobContext(cursor, factory.Clock.GetUtcNow(), new SystemActor("test")), CancellationToken.None);
    }

    public static async Task<Guid> AddAlert(
        VehicleApiFactory factory,
        string ruleId,
        string subjectType,
        Guid subjectId,
        string scopeKey,
        string severity,
        DateOnly? dueAt,
        string assignedRole = "dispatcher",
        string status = "Open",
        DateTimeOffset? snoozedUntil = null,
        string? snoozeReason = null,
        int version = 1)
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into fleetops.alerts (id, rule_id, subject_type, subject_id, scope_key, severity, due_at, assigned_role, status, snoozed_until, snooze_reason, opened_at, version) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)", connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(ruleId);
        command.Parameters.AddWithValue(subjectType);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(scopeKey);
        command.Parameters.AddWithValue(severity);
        command.Parameters.AddWithValue((object?)dueAt ?? DBNull.Value);
        command.Parameters.AddWithValue(assignedRole);
        command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue((object?)snoozedUntil ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)snoozeReason ?? DBNull.Value);
        command.Parameters.AddWithValue(factory.Clock.GetUtcNow());
        command.Parameters.AddWithValue(version);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    public static async Task SetVehicleStatus(VehicleApiFactory factory, Guid id, string status)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("update fleetops.vehicles set status = $1 where id = $2", connection);
        command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue(id);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task SetDocumentStatus(VehicleApiFactory factory, Guid id, string status)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("update fleetops.documents set status = $1 where id = $2", connection);
        command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue(id);
        await command.ExecuteNonQueryAsync();
    }

    private static string TypeName(DocumentType type) => type switch
    {
        DocumentType.HuReport => "hu_report",
        DocumentType.SpReport => "sp_report",
        DocumentType.TachoCalibration => "tacho_calibration",
        DocumentType.CommunityLicence => "community_licence",
        DocumentType.LicenceC_CE => "licence_c_ce",
        DocumentType.DriverCard => "driver_card",
        DocumentType.Code95 => "code95",
        DocumentType.Medical => "medical",
        DocumentType.Registration => "registration",
        DocumentType.Insurance => "insurance",
        _ => type.ToString().ToLowerInvariant(),
    };
}
