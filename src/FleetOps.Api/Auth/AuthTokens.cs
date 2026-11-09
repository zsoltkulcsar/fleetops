using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FleetOps.Kernel.Authorization;

namespace FleetOps.Api.Auth;

public static class AuthTokens
{
    public static string New() => FleetOps.Identity.SessionTokens.New();
    public static string Hash(string value) => FleetOps.Identity.SessionTokens.Hash(value);
    public static IReadOnlySet<RoleCode> ParseRoles(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json)?.Select(ParseRole).ToHashSet() ?? new HashSet<RoleCode>();
        }
        catch
        {
            return new HashSet<RoleCode>();
        }
    }
    public static RoleCode ParseRole(string role) => role.ToLowerInvariant() switch
    {
        "driver" => RoleCode.Driver,
        "dispatcher" => RoleCode.Dispatcher,
        "hr" => RoleCode.Hr,
        "owner" => RoleCode.Owner,
        _ => throw new FormatException()
    };
}
