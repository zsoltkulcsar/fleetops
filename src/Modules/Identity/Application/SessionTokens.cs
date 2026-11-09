using System.Security.Cryptography;
using System.Text;
namespace FleetOps.Identity;
public static class SessionTokens
{
    public const string PrivacyVersion = "placeholder-v1";
    public static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
