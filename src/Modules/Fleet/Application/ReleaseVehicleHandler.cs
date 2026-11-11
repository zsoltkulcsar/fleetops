using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;
namespace FleetOps.Modules.Fleet;
public sealed class ReleaseVehicleHandler(IVehicleReleaseStore store, IReleaseSettings settings, IVehicleReleaseReview review, ICompanyTimeService companyTime, TimeProvider time) : ICommandHandler<ReleaseVehicleCommand, VehicleReleaseResult>
{
    public async Task<VehicleReleaseResult> HandleAsync(ReleaseVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 500 || command.Reason.Any(char.IsControl)) throw new VehicleValidationException(["reason"]);
        var now = time.GetUtcNow();
        var today = companyTime.ToCompanyDate(now);
        var zone = CompanyTimeService.Resolve("Europe/Berlin");
        DateTimeOffset EndOfDay(DateOnly day) => new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), zone), TimeSpan.Zero).AddTicks(-1);
        var validUntil = EndOfDay(today.AddDays(1));
        if (command.ValidUntil is DateTimeOffset requested)
        {
            var local = companyTime.ToCompanyTime(requested);
            var day = companyTime.ToCompanyDate(requested);
            if ((day != today && day != today.AddDays(1)) || local.TimeOfDay < new TimeSpan(23, 59, 59)) throw new VehicleValidationException(["validUntil"]);
            validUntil = EndOfDay(day);
        }
        var enabled = await settings.GetReleasableDocTypesAsync(context, cancellationToken);
        if (enabled.Count == 0) throw new VehicleReleaseConflictException("release_not_enabled");
        var facts = await store.LockFactsAsync(command.VehicleId, today, context, cancellationToken) ?? throw new VehicleReleaseConflictException("not_found");
        var expired = facts.Causes.Where(cause => cause.Code == ReadinessCauseCode.DocumentExpired).Select(cause => cause.DocumentType!).Distinct().ToArray();
        var forbidden = facts.Causes.Where(cause => cause.Code != ReadinessCauseCode.DocumentExpired || cause.DocumentType is "insurance" or "registration" || !enabled.Contains(cause.DocumentType!)).ToArray();
        if (!facts.ActiveTruck || expired.Length == 0 || forbidden.Length > 0)
            throw new VehicleReleaseConflictException("release_not_allowed", forbidden.Select(cause => cause.DocumentType ?? cause.Code.ToString()).Distinct().ToArray());
        var release = new VehicleOverride { Id = Guid.NewGuid(), VehicleId = command.VehicleId, By = actor.EmployeeId, Reason = command.Reason.Trim(), At = now, ValidUntil = validUntil.ToUniversalTime(), DocTypes = expired };
        await store.SaveAsync(release, context, cancellationToken);
        await review.OpenAsync(release, !actor.Roles.Contains(RoleCode.Owner), context, cancellationToken);
        return new(release.Id, release.VehicleId, release.By, await store.ActorNameAsync(actor.EmployeeId, context, cancellationToken), release.At, release.ValidUntil, release.Reason, release.DocTypes);
    }
}
