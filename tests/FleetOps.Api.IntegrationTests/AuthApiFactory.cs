using System.Buffers.Binary;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FleetOps.Api.Auth;
using FleetOps.Identity;
using FleetOps.Migrations;
using FleetOps.People;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FleetOps.Tests;

public sealed class AuthApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Correct Horse Battery Staple!";

    private TestPostgres? postgres;

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-06T10:00:00+00:00"));

    public string Origin => "https://localhost";

    public string RuntimeConnectionString => postgres?.ConnectionString
        ?? throw new InvalidOperationException("Database is not initialized.");

    /// <summary>A client with a cookie jar, as a browser would behave over HTTPS.</summary>
    public HttpClient Client => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri(Origin),
        HandleCookies = true,
    });

    /// <summary>A client that keeps no cookies, so a test controls the Cookie header itself.</summary>
    public HttpClient PlainClient(string baseAddress = "https://localhost") => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri(baseAddress),
        HandleCookies = false,
    });

    public async ValueTask InitializeAsync()
    {
        postgres = await TestPostgres.StartAsync(nameof(SignInTests));
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
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:FleetOps", RuntimeConnectionString);
        builder.UseSetting("Auth:PublicOrigin", Origin);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public async Task<OfficeUser> CreateEmployee(
        IEnumerable<string> roles,
        EmployeeStatus status = EmployeeStatus.Active,
        DateOnly? endDate = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetOpsDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OfficeUser>>();
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "Employee",
            Email = $"{Guid.NewGuid():N}@example.test",
            Roles = JsonSerializer.Serialize(roles),
            Status = status,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = endDate,
        };
        db.Employees.Add(employee);
        var user = new OfficeUser
        {
            Id = Guid.NewGuid(),
            UserName = employee.Email,
            Email = employee.Email,
            EmployeeId = employee.Id,
            EmailConfirmed = true,
            LockoutEnabled = true,
        };
        var result = await users.CreateAsync(user, Password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(";", result.Errors.Select(error => error.Description)));
        }

        await db.SaveChangesAsync();
        return user;
    }

    public Task<HttpResponseMessage> SignIn(HttpClient client, OfficeUser user, string password = Password) =>
        Send(client, HttpMethod.Post, "/api/v1/auth/sign-in", new AuthEndpoints.SignInRequest(user.Email!, password));

    /// <summary>The valid authenticator code for the user at the factory clock.</summary>
    public async Task<string> CurrentTotp(OfficeUser user)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OfficeUser>>();
        var current = await users.FindByIdAsync(user.Id.ToString())
            ?? throw new InvalidOperationException("User not found");
        var secret = await users.GetAuthenticatorKeyAsync(current)
            ?? throw new InvalidOperationException("Authenticator key not found");
        return TotpCode(DecodeBase32(secret), Clock.GetUtcNow());
    }

    /// <summary>RFC 6238 code (SHA-1, 6 digits, 30 second step) for an arbitrary key, even an empty one.</summary>
    public static string TotpCode(byte[] key, DateTimeOffset at)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, at.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0f;
        var value = ((hash[offset] & 0x7f) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    public string? ReadMailbox()
    {
        return Services.GetRequiredService<IMailbox>() is DemoMailbox mailbox ? mailbox.LastLink : null;
    }

    public Task<HttpResponseMessage> Send(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body = null,
        bool includeOrigin = true,
        bool includeCustomHeader = true,
        string? sessionToken = null,
        string? origin = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (includeOrigin)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin ?? Origin);
        }

        if (includeCustomHeader)
        {
            request.Headers.TryAddWithoutValidation("X-FleetOps-Request", "1");
        }

        if (sessionToken is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{AuthEndpoints.CookieName}={sessionToken}");
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static byte[] DecodeBase32(string secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var buffer = 0;
        var key = new List<byte>();
        foreach (var character in secret.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                key.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return key.ToArray();
    }
}

public sealed class FakeTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset utcNow = initial;

    public override DateTimeOffset GetUtcNow() => utcNow;

    public void SetUtcNow(DateTimeOffset value) => utcNow = value;

    public void Advance(TimeSpan value) => utcNow = utcNow.Add(value);
}
