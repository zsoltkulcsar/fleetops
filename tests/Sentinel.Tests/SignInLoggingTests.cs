using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FleetOps.Tests;

public sealed class SignInLoggingTests
{
    [Fact]
    public async Task Failed_sign_in_logs_no_password_email_or_token()
    {
        var logs = new Capture();
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureLogging(x => x.AddProvider(logs)));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/sign-in",
            new { email = "sentinel@example.test", password = "Password-SECRET-123!" },
            TestContext.Current.CancellationToken);
        var text = string.Join("\n", logs.Items);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Password-SECRET-123!", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel@example.test", text, StringComparison.Ordinal);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reset_flow_logs_no_token()
    {
        var logs = new Capture();
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureLogging(x => x.AddProvider(logs)));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/reset-password")
        {
            Content = JsonContent.Create(new { token = "RESET-TOKEN-SECRET", password = "New Password-SECRET-123!" })
        };
        request.Headers.TryAddWithoutValidation("Origin", "https://localhost");
        request.Headers.TryAddWithoutValidation("X-FleetOps-Request", "1");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = string.Join("\n", logs.Items);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("RESET-TOKEN-SECRET", text, StringComparison.Ordinal);
        Assert.DoesNotContain("New Password-SECRET-123!", text, StringComparison.Ordinal);
    }

    private sealed class Capture : ILoggerProvider
    {
        public ConcurrentBag<string> Items { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Logger(Items);
        public void Dispose() { }
        private sealed class Logger(ConcurrentBag<string> items) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) => items.Add(formatter(state, error));
            private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
        }
    }
}
