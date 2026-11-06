using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FleetOps.Api.Auth;
using FleetOps.Identity;
using FleetOps.Migrations;
using FleetOps.People;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace FleetOps.Tests;

public sealed class AuthApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private TestPostgres? _postgres;
    private string ConnectionString => _postgres?.ConnectionString ?? throw new InvalidOperationException("Database is not initialized");
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-06T10:00:00+00:00"));
    public string Origin => "https://localhost";

    public async ValueTask InitializeAsync()
    {
        _postgres = await TestPostgres.StartAsync(nameof(SignInTests));
    }

    public new async ValueTask DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:FleetOps", ConnectionString);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient Client => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri(Origin), HandleCookies = true });

    public async Task<OfficeUser> CreateEmployee(IEnumerable<string> roles, EmployeeStatus status = EmployeeStatus.Active, DateOnly? endDate = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetOpsDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OfficeUser>>();
        var employee = new Employee { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Employee", Email = $"{Guid.NewGuid():N}@example.test", Roles = System.Text.Json.JsonSerializer.Serialize(roles), Status = status, StartDate = new DateOnly(2026, 1, 1), EndDate = endDate };
        db.Employees.Add(employee);
        var user = new OfficeUser { Id = Guid.NewGuid(), UserName = employee.Email, Email = employee.Email, EmployeeId = employee.Id, EmailConfirmed = true, LockoutEnabled = true };
        var result = await users.CreateAsync(user, "Correct Horse Battery Staple!");
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(";", result.Errors.Select(x => x.Description)));
        await db.SaveChangesAsync();
        return user;
    }

    public Task<HttpResponseMessage> SignIn(HttpClient client, OfficeUser user, string password = "Correct Horse Battery Staple!") => Send(client, HttpMethod.Post, "/api/v1/auth/sign-in", new AuthEndpoints.SignInRequest(user.Email!, password));

    public async Task<string> CurrentTotp(OfficeUser user)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OfficeUser>>();
        var current = await users.FindByIdAsync(user.Id.ToString()) ?? throw new InvalidOperationException("User not found");
        var secret = await users.GetAuthenticatorKeyAsync(current) ?? throw new InvalidOperationException("Authenticator key not found");
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var buffer = 0;
        var key = new List<byte>();
        foreach (var character in secret.TrimEnd('=').ToUpperInvariant())
        {
            var index = alphabet.IndexOf(character);
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8) { bits -= 8; key.Add((byte)(buffer >> bits)); buffer &= (1 << bits) - 1; }
        }
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, Clock.GetUtcNow().ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(key.ToArray(), counter);
        var offset = hash[^1] & 0x0f;
        var value = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    public string? ReadMailbox()
    {
        return Services.GetRequiredService<IMailbox>() is DemoMailbox mailbox ? mailbox.LastLink : null;
    }

    public Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body = null, bool includeOrigin = true, bool includeCustomHeader = true)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (includeOrigin)
            request.Headers.TryAddWithoutValidation("Origin", Origin);
        if (includeCustomHeader)
            request.Headers.TryAddWithoutValidation("X-FleetOps-Request", "1");
        return client.SendAsync(request);
    }

}

public sealed class FakeTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset _utcNow = initial;
    public override DateTimeOffset GetUtcNow() => _utcNow;
    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
}
