using System.Net.Http;

namespace FleetOps.Tests;

internal static class FleetRequestHeaders
{
    public const string Origin = "https://localhost";
    public static void Apply(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("Origin", Origin);
        request.Headers.TryAddWithoutValidation("X-FleetOps-Request", "1");
    }
    public static void Apply(HttpClient client)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", Origin);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-FleetOps-Request", "1");
    }
}
