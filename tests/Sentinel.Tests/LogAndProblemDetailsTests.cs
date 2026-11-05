using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    private sealed class CaptureLogger(ConcurrentBag<string> entries, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => entries.Add($"{categoryName} {logLevel} {formatter(state, exception)} {exception}");
    }
}
