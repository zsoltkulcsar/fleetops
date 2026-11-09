using System.Text.Json;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;
namespace FleetOps.Identity;
public static class EnrolmentRules
{
    public static bool Eligible(EnrolmentEmployee? employee, DateTimeOffset now, ICompanyTimeService clock)
    {
        if (employee is null || !employee.Active || employee.EndDate is DateOnly end && clock.ToCompanyDate(now) > end)
            return false;
        try
        {
            return JsonSerializer.Deserialize<string[]>(employee.Roles)?.Contains("driver", StringComparer.Ordinal) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
    public static async Task<int> PositiveSetting(ISettings settings, string key, CommandContext? context, CancellationToken ct)
    {
        var value = await settings.GetIntAsync(new(key), context, ct);
        return value > 0 ? value : throw new InvalidOperationException("Invalid authentication setting.");
    }
}
