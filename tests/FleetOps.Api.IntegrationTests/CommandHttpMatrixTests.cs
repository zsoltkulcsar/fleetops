using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api.Pipeline;
using FleetOps.Kernel.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FleetOps.Tests;

public sealed class CommandHttpMatrixTests
{
    private static readonly Guid ActorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherEmployeeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string EntitySentinel = "ENTITY_DATA_MUST_NOT_LEAK";

    [Fact]
    public async Task Production_wiring_uses_no_session_and_resolves_dispatchers()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ICommandPolicyProvider>(new FixedPolicies(
                    new CommandPolicy("shifts.approve_reject", new[] { RoleCode.Dispatcher }.ToHashSet())));
                services.AddSingleton<ICommandHandler<TestCommand, TestResult>, CountingCommandHandler>();
                services.AddSingleton<IStartupFilter>(new CommandEndpointStartupFilter());
            });
        });
        using var scope = factory.Services.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<CommandDispatcher>();
        _ = scope.ServiceProvider.GetRequiredService<QueryDispatcher>();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/__tests/command",
            new TestCommand(OtherEmployeeId, EntitySentinel, ActorId), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("not_signed_in", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("not_signed_in", 401, false, false)]
    [InlineData("permission_denied", 403, false, false)]
    [InlineData("own_other_employee", 403, false, false)]
    [InlineData("self_approval", 403, false, true)]
    [InlineData("state_conflict", 409, false, false)]
    [InlineData("payload_actor", 200, true, false)]
    [InlineData("allowed", 200, true, false)]
    public async Task Http_theory_parses_json_and_asserts_exact_refusal_code_and_allowed_roles(
        string scenario, int expectedStatus, bool allowed, bool selfApproval)
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(CommandHttpMatrixTests), TestContext.Current.CancellationToken);
        var calls = new CountingCommandHandler();
        var logs = new CaptureLoggerProvider();
        var accessor = new TestAccessor(scenario == "not_signed_in" ? null : AccessContext.For(ActorId,
            scenario == "permission_denied" ? RoleCode.Hr : RoleCode.Driver));
        var policy = new CommandPolicy("notifications.mine", new[] { RoleCode.Driver }.ToHashSet(),
            subjectRelation: SubjectRelation.Own, ownRoles: new[] { RoleCode.Driver }.ToHashSet(),
            separation: selfApproval ? SeparationOfDuty.OwnerRoutesToHr : SeparationOfDuty.None,
            subjectId: command => ((TestCommand)command).SubjectId);
        if (scenario == "state_conflict")
            policy = new CommandPolicy("notifications.mine", new[] { RoleCode.Driver }.ToHashSet(), SubjectRelation.Own,
                statePrecondition: (_, _) => false, subjectId: command => ((TestCommand)command).SubjectId,
                ownRoles: new[] { RoleCode.Driver }.ToHashSet());

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:FleetOps", postgres.ConnectionString);
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAccessContextAccessor>(accessor);
                services.AddSingleton<ICommandPolicyProvider>(new FixedPolicies(policy));
                services.AddSingleton<ICommandHandler<TestCommand, TestResult>>(calls);
                services.AddSingleton<IStartupFilter>(new CommandEndpointStartupFilter());
            });
        });
        using var client = factory.CreateClient();
        var subjectId = selfApproval || scenario is "allowed" or "payload_actor" or "state_conflict" ? ActorId : OtherEmployeeId;
        using var response = await client.PostAsJsonAsync("/__tests/command", new TestCommand(subjectId, EntitySentinel, scenario == "payload_actor" ? OtherEmployeeId : ActorId),
            cancellationToken: TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (!allowed)
        {
            using var json = JsonDocument.Parse(body);
            var expectedCode = scenario == "not_signed_in" ? "not_signed_in" : scenario == "state_conflict" ? "state_conflict" : scenario == "self_approval" ? "self_approval" : "permission_denied";
            Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
            var roles = json.RootElement.GetProperty("allowedRoles").EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.Equal(expectedCode == "not_signed_in" ? Array.Empty<string>() : new[] { "driver" }, roles);
        }

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.DoesNotContain(EntitySentinel, body, StringComparison.Ordinal);
        Assert.Equal(allowed ? 1 : 0, calls.Count);
        if (scenario == "permission_denied") Assert.Contains("driver", body, StringComparison.Ordinal);
        if (scenario == "allowed")
        {
            Assert.Equal(ActorId, calls.ActorId);
            Assert.Equal(subjectId, calls.SubjectId);
        }
        Assert.DoesNotContain(logs.Entries, x => x.Contains("Unhandled exception", StringComparison.Ordinal));
    }

    private sealed record TestCommand(Guid SubjectId, string Payload, Guid PayloadActor) : ICommand<TestResult>;
    private sealed record TestResult(bool Accepted, Guid ActorId);

    private sealed class CountingCommandHandler : ICommandHandler<TestCommand, TestResult>
    {
        public int Count { get; private set; }
        public Guid ActorId { get; private set; }
        public Guid SubjectId { get; private set; }
        public Task<TestResult> HandleAsync(TestCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        {
            Count++;
            ActorId = actor.EmployeeId;
            SubjectId = command.SubjectId;
            return Task.FromResult(new TestResult(true, actor.EmployeeId));
        }
    }

    private sealed class CommandEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (context, continuation) =>
            {
                if (!context.Request.Path.StartsWithSegments("/__tests/command"))
                {
                    await continuation();
                    return;
                }
                var command = await JsonSerializer.DeserializeAsync<TestCommand>(context.Request.Body,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken: context.RequestAborted)
                    ?? throw new InvalidOperationException("Missing command payload");
                var dispatcher = context.RequestServices.GetRequiredService<CommandDispatcher>();
                var result = await dispatcher.DispatchAsync<TestCommand, TestResult>(command, context.RequestAborted);
                await Results.Json(result).ExecuteAsync(context);
            });
        };
    }

    private sealed class TestAccessor(AccessContext? context) : IAccessContextAccessor { public AccessContext? Current => context; }
    private sealed class FixedPolicies(CommandPolicy policy) : ICommandPolicyProvider { public CommandPolicy? GetPolicy(Type commandType) => policy; }
    private sealed class TestTransactionFactory : ICommandTransactionFactory
    {
        public Task<ICommandTransaction> BeginAsync(CancellationToken cancellationToken) => Task.FromResult<ICommandTransaction>(new TestTransaction());
    }
    private sealed class TestTransaction : ICommandTransaction
    {
        public CommandContext Context { get; } = new(null!, null!);
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Entries);
        public void Dispose() { }
    }

    private sealed class CaptureLogger(ConcurrentBag<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => entries.Add(formatter(state, exception));
    }
}
