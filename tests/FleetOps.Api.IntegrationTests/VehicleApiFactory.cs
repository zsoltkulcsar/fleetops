using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;
using FleetOps.Migrations;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

/// <summary>
/// Hosts the API against a fresh migrated database. The host connects as fleetops_runtime;
/// the schema comes only from migrations run as fleetops_migrator (see <see cref="TestPostgres"/>).
/// The signed-in actor is set per test through <see cref="AsDispatcher"/> and its siblings.
/// </summary>
public sealed class VehicleApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid ActorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    private static readonly HashSet<string> CountableTables =
    [
        "vehicles", "audit_entries", "vehicle_plate_history", "depots",
        "documents", "document_files", "pending_files",
    ];

    private static int sequence;

    private readonly MutableAccessor accessor = new();
    private TestPostgres? postgres;

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-06T10:00:00+00:00"));

    public FakeObjectStore ObjectStore { get; } = new();

    public bool UseRealObjectStore { get; set; }
    public bool FreshStepUp { get => accessor.HasFreshStepUp; set => accessor.HasFreshStepUp = value; }

    public ConcurrentQueue<string>? LogItems { get; set; }

    public IList<IAlertEvaluator> AdditionalAlertEvaluators { get; } = new List<IAlertEvaluator>();

    /// <summary>When set, the next write command runs inside its transaction and then throws.</summary>
    public bool ThrowAfterWrite { get; set; }

    public bool ThrowAfterNotificationWrite { get; set; }

    public bool ThrowNotificationSender { get; set; }

    public string RuntimeConnectionString => postgres?.ConnectionString
        ?? throw new InvalidOperationException("Database is not initialized.");

    /// <summary>Owner-level connection, for test-only fixture tables that are not part of the migrations.</summary>
    public string AdminConnectionString => postgres?.AdminConnectionString
        ?? throw new InvalidOperationException("Database is not initialized.");

    public async ValueTask InitializeAsync()
    {
        postgres = await TestPostgres.StartAsync(nameof(VehicleApiFactory), TestContext.Current.CancellationToken);
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (postgres is not null)
        {
            await postgres.DisposeAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:FleetOps", RuntimeConnectionString);
        builder.UseSetting("Auth:PublicOrigin", FleetRequestHeaders.Origin);
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            if (LogItems is not null)
                logging.AddProvider(new CaptureProvider(LogItems));
        });
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAccessContextAccessor>(accessor);
            foreach (var descriptor in services.Where(service => service.ServiceType == typeof(TimeProvider)).ToList())
                services.Remove(descriptor);
            services.AddSingleton<TimeProvider>(Clock);
            foreach (var descriptor in services.Where(service => service.ServiceType == typeof(IObjectStore)).ToList())
                services.Remove(descriptor);
            if (!UseRealObjectStore)
                services.AddSingleton<IObjectStore>(ObjectStore);
            else
                services.AddScoped<IObjectStore, FleetOps.Api.Files.S3ObjectStore>();
            services.AddScoped<IVehicleStore>(provider => ThrowAfterWrite
                ? new FailingVehicleStore(new EfVehicleStore(Clock, provider.GetRequiredService<FleetOps.Kernel.IChangedSubjects>()), () => ThrowAfterWrite = false)
                : new EfVehicleStore(Clock, provider.GetRequiredService<FleetOps.Kernel.IChangedSubjects>()));
            foreach (var evaluator in AdditionalAlertEvaluators)
                services.AddSingleton<IAlertEvaluator>(evaluator);
            if (ThrowAfterNotificationWrite)
            {
                services.RemoveAll<INotifications>();
                services.AddScoped<INotifications>(provider => new ThrowingNotifications(
                    new EfNotifications(
                        provider.GetRequiredService<TimeProvider>(),
                        provider.GetRequiredService<INotificationIds>())));
            }
            if (ThrowNotificationSender)
            {
                services.RemoveAll<IPostCommitNotifications>();
                services.AddScoped<IPostCommitNotifications, ThrowingPostCommitNotifications>();
            }
        });
    }

    public HttpClient AsDispatcher() => ClientAs(RoleCode.Dispatcher);

    public HttpClient AsOwner() => ClientAs(RoleCode.Owner);

    public HttpClient AsHr() => ClientAs(RoleCode.Hr);

    public HttpClient AsHr(Guid employeeId) => ClientAs(employeeId, RoleCode.Hr);

    public HttpClient AsDriver() => ClientAs(RoleCode.Driver);
    public HttpClient AsDriver(Guid employeeId) => ClientAs(employeeId, RoleCode.Driver);

    /// <summary>A client that sends the origin headers but, until a test calls AsX, has no signed-in actor.</summary>
    public HttpClient Anonymous() => ClientWithHeaders();

    /// <summary>A plate that no other call in this process has used.</summary>
    public string NextPlate() => $"B-X {Interlocked.Increment(ref sequence):0000}";

    /// <summary>A valid VIN that no other call in this process has used.</summary>
    public string NextVin() => $"WVWZZZ1JZXW{Interlocked.Increment(ref sequence):000000}";

    public async Task<VehicleDetail> CreateTruck(
        HttpClient client,
        string? plate = null,
        string? vin = null,
        string reason = "create reason",
        int grossWeightKg = 8000)
    {
        var number = Interlocked.Increment(ref sequence);
        plate ??= $"B-T {number:0000}";
        vin ??= $"WVWZZZ1JZXW{number:000000}";
        using var response = await client.PostAsJsonAsync(
            "/api/v1/fleet/vehicles",
            new
            {
                kind = "truck",
                plate,
                vin,
                make = "MAN",
                model = "TGX",
                year = 2020,
                grossWeightKg,
                requiredLicenceClass = "C",
                ownership = "owned",
                leaseEndDate = (DateOnly?)null,
                trailerType = (string?)null,
                odometerKm = 1000,
                depotId = (Guid?)null,
                serviceIntervalKm = 50000,
                serviceIntervalMonths = 12,
                // A recent download keeps A-12 ("No download logged yet", Story 9.5) out of tests about other alerts.
                lastTachoDownloadAt = (DateOnly?)DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime),
                reason,
            },
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetail>(JsonOptions, TestContext.Current.CancellationToken))!;
    }

    public async Task<long> CountRows(string table)
    {
        if (!CountableTables.Contains(table))
        {
            throw new ArgumentException($"Table {table} is not countable.", nameof(table));
        }

        await using var connection = await DirectConnection();
        await using var command = new NpgsqlCommand($"select count(*) from fleetops.{table}", connection);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>An independent connection as fleetops_runtime, for reading what the API wrote.</summary>
    public async Task<NpgsqlConnection> DirectConnection()
    {
        var connection = new NpgsqlConnection(RuntimeConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private HttpClient ClientAs(RoleCode role)
    {
        accessor.Current = AccessContext.For(ActorId, role);
        return ClientWithHeaders();
    }

    private HttpClient ClientAs(Guid employeeId, RoleCode role)
    {
        accessor.Current = AccessContext.For(employeeId, role);
        return ClientWithHeaders();
    }

    private HttpClient ClientWithHeaders()
    {
        var client = CreateClient();
        FleetRequestHeaders.Apply(client);
        return client;
    }

    private sealed class MutableAccessor : IAccessContextAccessor, IStepUpContextAccessor
    {
        public AccessContext? Current { get; set; }
        public bool HasFreshStepUp { get; set; }
    }

    private sealed class FailingVehicleStore(EfVehicleStore inner, Action clearFailure) : IVehicleStore
    {
        public async Task<VehicleDetail> CreateAsync(
            CreateVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        {
            _ = await inner.CreateAsync(command, actor, context, cancellationToken);
            clearFailure();
            throw new InvalidOperationException("after write");
        }

        public async Task<VehicleDetail> EditAsync(
            EditVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        {
            _ = await inner.EditAsync(command, actor, context, cancellationToken);
            clearFailure();
            throw new InvalidOperationException("after write");
        }

        public async Task<VehicleDetail> RetireAsync(
            RetireVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        {
            _ = await inner.RetireAsync(command, actor, context, cancellationToken);
            clearFailure();
            throw new InvalidOperationException("after write");
        }

        public async Task<VehicleDetail> UndoRetireAsync(
            UndoRetireVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        {
            _ = await inner.UndoRetireAsync(command, actor, context, cancellationToken);
            clearFailure();
            throw new InvalidOperationException("after write");
        }
    }

    private sealed class ThrowingNotifications(INotifications inner) : INotifications
    {
        public async Task<Guid> AddAsync(Guid employeeId, string templateCode, object parameters, Guid? alertId, CommandContext context, CancellationToken cancellationToken)
        {
            var id = await inner.AddAsync(employeeId, templateCode, parameters, alertId, context, cancellationToken);
            throw new InvalidOperationException("after notification write");
        }
    }

    private sealed class ThrowingPostCommitNotifications(IConfiguration configuration, TimeProvider time) : IPostCommitNotifications
    {
        public async Task TrySendAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        {
            var connectionString = configuration.GetConnectionString("FleetOps")!;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("update fleetops.notifications set attempts = attempts + 1, next_attempt_at = $1 + interval '1 minute' where id = any($2::uuid[]) and sent_at is null", connection);
            command.Parameters.AddWithValue(time.GetUtcNow());
            command.Parameters.AddWithValue(ids.ToArray());
            await command.ExecuteNonQueryAsync(cancellationToken);
            throw new InvalidOperationException("notification sender failure");
        }
    }

    private sealed class CaptureProvider(ConcurrentQueue<string> items) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(items);
        public void Dispose() { }

        private sealed class CaptureLogger(ConcurrentQueue<string> items) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => items.Enqueue($"{formatter(state, exception)} {exception}");
            private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
        }
    }
}
