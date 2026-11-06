namespace FleetOps.Api.Auth;

public static class OriginGuard
{
    public static bool IsUnsafe(HttpRequest request) => request.Method is not ("GET" or "HEAD" or "OPTIONS");
    public static bool IsAllowed(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        var expected = $"{request.Scheme}://{request.Host}";
        return !string.IsNullOrWhiteSpace(origin) && string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase) && request.Headers.ContainsKey("X-FleetOps-Request");
    }
}
