using FleetOps.Modules.Alerts;
using FleetOps.Modules.Documents;
using System.Net.Http.Json;
using Xunit;

namespace FleetOps.Tests;

public sealed class AlertPipelineTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();

    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Evaluators_run_in_the_commands_transaction()
    {
        factory.ThrowAfterWrite = true;
        using var response = await factory.AsDispatcher().PostAsJsonAsync("/api/v1/fleet/vehicles", new { kind = "truck", plate = factory.NextPlate(), vin = factory.NextVin(), make = "MAN", model = "TGX", year = 2020, grossWeightKg = 8000, requiredLicenceClass = "C", ownership = "owned", odometerKm = 1000, reason = "failure" });
        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(0, await factory.CountRows("vehicles"));
        Assert.Equal(0, await AlertRows(factory));
    }

    [Fact]
    public async Task Failing_evaluator_does_not_fail_the_command()
    {
        factory.AdditionalAlertEvaluators.Add(new ThrowingEvaluator());
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        Assert.NotEqual(Guid.Empty, vehicle.Id);
        Assert.Equal(0, await AlertTestSupport.Count(factory, "X-FAIL", vehicle.Id));
    }

    [Fact]
    public async Task Failing_evaluator_is_logged_by_type_only()
    {
        var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        factory.LogItems = logs;
        factory.AdditionalAlertEvaluators.Add(new ThrowingEvaluator());
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        Assert.NotEqual(Guid.Empty, vehicle.Id);
        Assert.Contains(logs, x => x.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        Assert.DoesNotContain(logs, x => x.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Failing_evaluator_does_not_stop_the_others()
    {
        factory.AdditionalAlertEvaluators.Add(new ThrowingEvaluator());
        factory.AdditionalAlertEvaluators.Add(new SuccessfulEvaluator());
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        Assert.Equal("Open", (await AlertTestSupport.Alert(factory, "X-OK", "vehicle", vehicle.Id, "success")).Status);
    }

    [Fact]
    public async Task Failing_evaluator_leaves_no_partial_write()
    {
        factory.AdditionalAlertEvaluators.Add(new WritingThenThrowingEvaluator());
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        Assert.Equal(0, await AlertTestSupport.Count(factory, "X-PARTIAL", vehicle.Id));
    }

    [Fact]
    public async Task Document_change_reaches_its_owner()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await ReadinessTestSupport.AddAllVehicleDocuments(factory, vehicle.Id, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(30));
        await using var connection = await factory.DirectConnection();
        await using var command = new Npgsql.NpgsqlCommand("update fleetops.documents set expires_at = date '2026-11-01' where owner_id = $1 and type = 'hu_report'", connection);
        command.Parameters.AddWithValue(vehicle.Id);
        await command.ExecuteNonQueryAsync();
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        Assert.Equal("Open", (await AlertTestSupport.Alert(factory, "A-05", "vehicle", vehicle.Id, "hu_report")).Status);
    }

    [Fact]
    public async Task Command_and_sweep_racing_leave_one_alert()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(-1));
        for (var attempt = 0; attempt < 20; attempt++)
            await Task.WhenAll(AlertTestSupport.Apply(factory, "vehicle", vehicle.Id), AlertTestSupport.Sweep(factory));
        Assert.InRange(await AlertTestSupport.Count(factory, "A-05", vehicle.Id), 0, 1);
    }

    [Fact]
    public async Task Concurrent_engines_do_not_fail()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(-1));
        var runs = Enumerable.Range(0, 8).Select(_ => AlertTestSupport.Apply(factory, "vehicle", vehicle.Id));
        await Task.WhenAll(runs);
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-05", vehicle.Id));
    }

    [Fact]
    public async Task No_change_writes_nothing()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(-1));
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        var before = await AlertTestSupport.Alert(factory, "A-05", "vehicle", vehicle.Id, "hu_report");
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        var after = await AlertTestSupport.Alert(factory, "A-05", "vehicle", vehicle.Id, "hu_report");
        Assert.Equal(before.Xmin, after.Xmin);
        Assert.Equal(before.OpenedAt, after.OpenedAt);
    }

    private static async Task<long> AlertRows(VehicleApiFactory factory)
    {
        await using var connection = await factory.DirectConnection();
        await using var command = new Npgsql.NpgsqlCommand("select count(*) from fleetops.alerts", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class ThrowingEvaluator : IAlertEvaluator
    {
        public string RuleId => "X-FAIL";
        public string SubjectType => "vehicle";
        public Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken) => throw new InvalidOperationException("secret evaluator message");
    }

    private sealed class SuccessfulEvaluator : IAlertEvaluator
    {
        public string RuleId => "X-OK";
        public string SubjectType => "vehicle";
        public Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AlertCondition>>([new("success", AlertSeverity.Warning, null)]);
    }

    private sealed class WritingThenThrowingEvaluator : IAlertEvaluator
    {
        public string RuleId => "X-PARTIAL";
        public string SubjectType => "vehicle";
        public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
        {
            await using var command = new Npgsql.NpgsqlCommand("insert into fleetops.alerts (id, rule_id, subject_type, subject_id, scope_key, severity, assigned_role, status, opened_at, version) values ($1, 'X-PARTIAL', 'vehicle', $2, 'partial', 'Warning', 'dispatcher', 'Open', $3, 1)", (Npgsql.NpgsqlConnection)context.Command.DbConnection, (Npgsql.NpgsqlTransaction)context.Command.DbTransaction);
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(subjectId);
            command.Parameters.AddWithValue(now);
            await command.ExecuteNonQueryAsync(cancellationToken);
            throw new InvalidOperationException("partial evaluator message");
        }
    }
}

