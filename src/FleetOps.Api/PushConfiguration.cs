using FleetOps.Modules.Notifications;
namespace FleetOps.Api;
public sealed class ConfiguredPush(IConfiguration configuration) : IPushConfiguration
{
    public PushConfiguration Get()
    {
        var publicKey = configuration["Push:VapidPublicKey"];
        var privateKey = configuration["Push:VapidPrivateKey"];
        var enabled = Valid(publicKey, 65) && Valid(privateKey, 32);
        return new(enabled, enabled ? publicKey : null);
    }
    private static bool Valid(string? key, int size)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        try
        {
            var normalized = key.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '=')).Length == size;
        }
        catch (FormatException) { return false; }
    }
}
