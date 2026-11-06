using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
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
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => { b.UseSetting("Auth:PublicOrigin", "https://localhost"); b.ConfigureLogging(x => x.AddProvider(logs)); });
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
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => { b.UseSetting("Auth:PublicOrigin", "https://localhost"); b.ConfigureLogging(x => x.AddProvider(logs)); });
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

    [Fact]
    public async Task Production_logs_only_allow_listed_categories()
    {
        var logs = new Capture();
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:PublicOrigin", "https://localhost");
            builder.UseSetting("ConnectionStrings:FleetOps", UnreachableDatabase);
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var loggers = app.Services.GetRequiredService<ILoggerFactory>();
        loggers.CreateLogger("Npgsql.Probe").LogError("probe from a library category");
        loggers.CreateLogger("Microsoft.AspNetCore.Probe").LogError("probe from a framework category");
        loggers.CreateLogger("FleetOps.Probe").LogError("probe from an allowed category");
        loggers.CreateLogger("Microsoft.Hosting.Lifetime").LogError("probe from the lifetime category");

        using var signIn = await SendWithOriginHeaders(client, "/api/v1/auth/sign-in", new { email = "sentinel@example.test", password = "Password-SECRET-123!" });
        using var sessionRead = await SendWithOriginHeaders(client, "/api/v1/auth/session", body: null, cookie: new string('c', 43), method: HttpMethod.Get);

        Assert.True(signIn.StatusCode >= HttpStatusCode.BadRequest, "The database is unreachable, so sign-in cannot succeed.");
        Assert.Equal(HttpStatusCode.InternalServerError, signIn.StatusCode);
        var categories = logs.Categories.Distinct().ToArray();
        Assert.Contains("FleetOps.Probe", categories);
        Assert.Contains("Microsoft.Hosting.Lifetime", categories);
        Assert.DoesNotContain("Npgsql.Probe", categories);
        Assert.DoesNotContain("Microsoft.AspNetCore.Probe", categories);
        Assert.All(categories, category => Assert.True(
            category.StartsWith("FleetOps.", StringComparison.Ordinal) || category == "Microsoft.Hosting.Lifetime",
            $"Category {category} is not on the allow list."));
        var everything = string.Join("\n", logs.Items);
        Assert.DoesNotContain("Npgsql", everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("Password-SECRET-123!", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("DB-PASSWORD-SENTINEL", everything, StringComparison.Ordinal);
        Assert.Contains(logs.Items, item => item.Contains("Unhandled exception", StringComparison.Ordinal) && item.Contains("TraceId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Second_factor_code_and_cookie_never_reach_logs()
    {
        const string codeSentinel = "918273";
        var cookieSentinel = "COOKIE-SENTINEL-" + new string('x', 27);
        Assert.Equal(43, cookieSentinel.Length);
        var logs = new Capture();
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:PublicOrigin", "https://localhost");
            builder.UseSetting("ConnectionStrings:FleetOps", UnreachableDatabase);
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var secondFactor = await SendWithOriginHeaders(client, "/api/v1/auth/second-factor", new { code = codeSentinel }, cookieSentinel);
        using var stepUp = await SendWithOriginHeaders(client, "/api/v1/auth/step-up", new { code = codeSentinel }, cookieSentinel);

        Assert.Equal(HttpStatusCode.InternalServerError, secondFactor.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, stepUp.StatusCode);
        var everything = string.Join("\n", logs.Items);
        Assert.NotEmpty(logs.Items);
        Assert.DoesNotContain(codeSentinel, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(cookieSentinel, everything, StringComparison.Ordinal);
        Assert.DoesNotContain("COOKIE-SENTINEL", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("__Host-fleetops_session", everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configuration_cannot_widen_the_logging_allow_list()
    {
        var logs = new Capture();
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Logging:LogLevel:Npgsql", "Debug");
            builder.UseSetting("ConnectionStrings:FleetOps", UnreachableDatabase);
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        loggerFactory.CreateLogger("Npgsql.Probe").LogError("probe from a disallowed category");
        loggerFactory.CreateLogger("FleetOps.Probe").LogError("probe from an allowed category");

        Assert.DoesNotContain("Npgsql.Probe", logs.Categories);
        Assert.Contains("FleetOps.Probe", logs.Categories);
        Assert.DoesNotContain("probe from a disallowed category", logs.Items);
    }

    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=sentinel;Username=sentinel;Password=DB-PASSWORD-SENTINEL;Timeout=1;Command Timeout=1";

    private static Task<HttpResponseMessage> SendWithOriginHeaders(HttpClient client, string path, object? body, string? cookie = null, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.TryAddWithoutValidation("Origin", "https://localhost");
        request.Headers.TryAddWithoutValidation("X-FleetOps-Request", "1");
        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", "__Host-fleetops_session=" + cookie);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class Capture : ILoggerProvider
    {
        public ConcurrentBag<string> Items { get; } = [];
        public ConcurrentBag<string> Categories { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Logger(Items, Categories, categoryName);
        public void Dispose() { }
        private sealed class Logger(ConcurrentBag<string> items, ConcurrentBag<string> categories, string category) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) { categories.Add(category); items.Add(formatter(state, error) + (error is null ? "" : "\n" + error)); }
            private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
        }
    }
}
