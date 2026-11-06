using FleetOps.Api.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FleetOps.Tests;

public sealed class AuthArchitectureTests
{
    [Fact]
    public async Task Auth_endpoints_are_the_only_endpoints_without_a_session()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        AuthEndpoints.Map(app);
        await app.StartAsync();
        var routes = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().Select(x => x.RoutePattern.RawText).Where(x => x is not null).ToArray();
        Assert.Contains("/api/v1/auth/sign-in", routes);
        Assert.Contains("/api/v1/auth/session", routes);
        Assert.DoesNotContain(routes, route => route!.StartsWith("/api/v1/", StringComparison.Ordinal) && !route.StartsWith("/api/v1/auth/", StringComparison.Ordinal));
        await app.StopAsync();
    }
}
