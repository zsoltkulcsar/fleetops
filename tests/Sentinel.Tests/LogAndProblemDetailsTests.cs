using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FleetOps.Kernel.Authorization;
using FleetOps.Api.Pipeline;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;
using Xunit;

namespace FleetOps.Tests;

public sealed class LogAndProblemDetailsTests
{
    [Fact]
    public async Task Sentinel_values_are_absent_from_logs_and_problem_details()
    {
        const string sentinel = "SENTINEL_PERSONAL_VALUE";
        const string control = "CONTROL_VALUE_MUST_BE_CAPTURED";
        var provider = new CaptureLoggerProvider();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(provider));
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new SentinelEndpointStartupFilter()));
        });
        using var client = factory.CreateClient();
        using var safeRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/__tests/sentinel?value={Uri.EscapeDataString(sentinel)}");
        safeRequest.Headers.Add("X-Test-Sentinel", sentinel);
        safeRequest.Content = new StringContent(sentinel, Encoding.UTF8, "text/plain");
        using var safeResponse = await client.SendAsync(safeRequest, TestContext.Current.CancellationToken);
        var safeBody = await safeResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var throwingRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/__tests/sentinel/throw?value={Uri.EscapeDataString(sentinel)}");
        throwingRequest.Headers.Add("X-Test-Sentinel", sentinel);
        throwingRequest.Content = new StringContent(sentinel, Encoding.UTF8, "text/plain");
        using var throwingResponse = await client.SendAsync(throwingRequest, TestContext.Current.CancellationToken);
        var problemBody = await throwingResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var logs = string.Join("\n", provider.Entries);
        Assert.Equal(204, (int)safeResponse.StatusCode);
        Assert.Empty(safeBody);
        Assert.Equal(500, (int)throwingResponse.StatusCode);
        Assert.Contains(control, logs);
        Assert.Contains("Unhandled exception InvalidOperationException TraceId", logs);
        Assert.DoesNotContain("test exception:", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, problemBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatcher_output_is_masked_by_role()
    {
        var employeeId = Guid.NewGuid();
        var accessor = new MutableAccessor(AccessContext.For(employeeId, RoleCode.Dispatcher));
        using var services = new ServiceCollection()
            .AddSingleton<IAccessContextAccessor>(accessor)
            .AddSingleton<ICommandPolicyProvider, DefaultCommandPolicyProvider>()
            .AddSingleton<IQueryHandler<MedicalSummaryQuery, DispatcherSummaryReadModel>, MedicalSummaryQueryHandler>()
            .AddSingleton<IQueryHandler<MedicalDetailQuery, MedicalDetailReadModel>, MedicalDetailQueryHandler>()
            .AddScoped<QueryDispatcher>()
            .BuildServiceProvider();
        var dispatcher = services.GetRequiredService<QueryDispatcher>();
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.Strict };
        var summary = await dispatcher.DispatchAsync<MedicalSummaryQuery, DispatcherSummaryReadModel>(new MedicalSummaryQuery(), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("MEDICAL_SENTINEL", JsonSerializer.Serialize(summary, options));
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            dispatcher.DispatchAsync<MedicalDetailQuery, MedicalDetailReadModel>(new MedicalDetailQuery(Guid.NewGuid()), TestContext.Current.CancellationToken));

        accessor.Current = AccessContext.For(employeeId, RoleCode.Hr);
        var detail = await dispatcher.DispatchAsync<MedicalDetailQuery, MedicalDetailReadModel>(new MedicalDetailQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);
        Assert.Contains("MEDICAL_SENTINEL", JsonSerializer.Serialize(detail, options));

        accessor.Current = AccessContext.For(Guid.NewGuid(), RoleCode.Driver);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            dispatcher.DispatchAsync<MedicalDetailQuery, MedicalDetailReadModel>(new MedicalDetailQuery(Guid.NewGuid()), TestContext.Current.CancellationToken));
        var ownSubject = ((AccessContext)accessor.Current!).EmployeeId;
        detail = await dispatcher.DispatchAsync<MedicalDetailQuery, MedicalDetailReadModel>(new MedicalDetailQuery(ownSubject), TestContext.Current.CancellationToken);
        Assert.Contains("MEDICAL_SENTINEL", JsonSerializer.Serialize(detail, options));

        accessor.Current = AccessContext.For(Guid.NewGuid(), RoleCode.Owner);
        detail = await dispatcher.DispatchAsync<MedicalDetailQuery, MedicalDetailReadModel>(new MedicalDetailQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);
        Assert.Contains("MEDICAL_SENTINEL", JsonSerializer.Serialize(detail, options));
    }

    private sealed class SentinelEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (context, continuation) =>
            {
                if (!context.Request.Path.StartsWithSegments("/__tests/sentinel")) { await continuation(); return; }
                using var reader = new StreamReader(context.Request.Body);
                _ = await reader.ReadToEndAsync(context.RequestAborted);
                _ = context.Request.Query["value"].ToString();
                _ = context.Request.Headers["X-Test-Sentinel"].ToString();
                var logger = context.RequestServices.GetRequiredService<ILogger<SentinelEndpointStartupFilter>>();
                logger.LogInformation("Test control value {ControlValue}", "CONTROL_VALUE_MUST_BE_CAPTURED");
                if (context.Request.Path.Value?.EndsWith("/throw", StringComparison.Ordinal) == true)
                    throw new InvalidOperationException($"test exception: {context.Request.Query["value"]}");
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            });
        };
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Entries, categoryName);
        public void Dispose() { }
    }

    private sealed class MutableAccessor(AccessContext? current) : IAccessContextAccessor
    {
        public AccessContext? Current { get; set; } = current;
    }

    [QueryPolicy("documents.medical.view")]
    private sealed class MedicalSummaryQuery : IQuery<DispatcherSummaryReadModel> { }

    [QueryPolicy("documents.medical.view")]
    private sealed record MedicalDetailQuery(Guid? SubjectId) : IQuery<MedicalDetailReadModel>, IHasSubject;

    [VisibleTo(RoleCode.Dispatcher)]
    private sealed class DispatcherSummaryReadModel(string sourceMedicalValue) : IReadModel
    {
        public string Status => sourceMedicalValue.Length > 0 ? "masked" : "masked";
    }

    [VisibleTo(RoleCode.Driver, RoleCode.Hr, RoleCode.Owner)]
    private sealed class MedicalDetailReadModel(string medical) : IReadModel { public string Medical => medical; }

    private sealed class MedicalSummaryQueryHandler : IQueryHandler<MedicalSummaryQuery, DispatcherSummaryReadModel>
    {
        public Task<DispatcherSummaryReadModel> HandleAsync(MedicalSummaryQuery query, AccessContext actor, CancellationToken cancellationToken)
        {
            var sourceMedicalValue = "MEDICAL_SENTINEL";
            return Task.FromResult(new DispatcherSummaryReadModel(sourceMedicalValue));
        }
    }

    private sealed class MedicalDetailQueryHandler : IQueryHandler<MedicalDetailQuery, MedicalDetailReadModel>
    {
        public Task<MedicalDetailReadModel> HandleAsync(MedicalDetailQuery query, AccessContext actor, CancellationToken cancellationToken)
        {
            var sourceMedicalValue = "MEDICAL_SENTINEL";
            return Task.FromResult(new MedicalDetailReadModel(sourceMedicalValue));
        }
    }

    private sealed class CaptureLogger(ConcurrentBag<string> entries, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => entries.Add($"{categoryName} {logLevel} {formatter(state, exception)} {exception}");
    }
}
