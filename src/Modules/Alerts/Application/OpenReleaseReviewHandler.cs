using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Notifications;
namespace FleetOps.Modules.Alerts;
public sealed class OpenReleaseReviewHandler(IReleaseReviewStore store, INotifications notifications) : IVehicleReleaseReview
{
    public async Task OpenAsync(VehicleOverride release, bool notifyOwner, CommandContext context, CancellationToken cancellationToken)
    {
        var alert = new Alert
        {
            Id = Guid.NewGuid(), RuleId = "A-13", SubjectType = "vehicle", SubjectId = release.VehicleId,
            ScopeKey = release.Id.ToString(), Severity = AlertSeverity.Warning, AssignedRole = notifyOwner ? "hr,owner" : "hr", OpenedAt = release.At
        };
        await store.AddAsync(alert, context, cancellationToken);
        foreach (var recipient in await store.RecipientsAsync(context, cancellationToken))
        {
            if (recipient.Roles.Contains(RoleCode.Hr) || notifyOwner && recipient.Roles.Contains(RoleCode.Owner))
                await notifications.AddAsync(recipient.Id, "vehicle_release_review", new { alertId = alert.Id, vehicleId = release.VehicleId, overrideId = release.Id }, alert.Id, context, cancellationToken);
        }
    }
}
