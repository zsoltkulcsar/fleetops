namespace FleetOps.Api.Auth;

public static class OriginGuard
{
    public static bool IsUnsafe(HttpRequest request) => request.Method is not ("GET" or "HEAD" or "OPTIONS");
    public static bool IsAllowed(HttpRequest request, string? configuredOrigin)
    {
        var origin = request.Headers.Origin.ToString();
        return !string.IsNullOrWhiteSpace(configuredOrigin) && string.Equals(origin, configuredOrigin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) && request.Headers.ContainsKey("X-FleetOps-Request");
    }
}
