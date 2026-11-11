using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Audit;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Notifications;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public static class ReleaseBanners
{
    public static async Task<ReleaseBanner?> ReadAsync(FleetOpsDbContext db, Guid vehicleId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var release = await db.VehicleOverrides.AsNoTracking().Where(release => release.VehicleId == vehicleId && release.At <= now && release.ValidUntil >= now).OrderByDescending(release => release.At).FirstOrDefaultAsync(cancellationToken);
        if (release is null) return null;
        var name = await db.Employees.Where(person => person.Id == release.By).Select(person => person.FirstName + " " + person.LastName).SingleAsync(cancellationToken);
        var until = TimeZoneInfo.ConvertTime(release.ValidUntil, FleetOps.Kernel.CompanyTimeService.Resolve("Europe/Berlin"));
        return new(release.Id, name, release.ValidUntil, $"Released by {name} until {until:dd.MM.}");
    }
}
