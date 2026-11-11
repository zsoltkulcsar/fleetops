using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;
using Microsoft.Extensions.Options;
namespace FleetOps.Identity;
public sealed class DriverConfigHandler(IEnrolmentStore store, ISettings settings, IOptions<EnrolmentOptions> options, IPersonAccessStore access, ICurrentSession session) : IQueryHandler<GetDriverConfigQuery, DriverConfig>
{
    public async Task<DriverConfig> HandleAsync(GetDriverConfigQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var employee = await store.FindEmployeeAsync(actor.EmployeeId, null, cancellationToken)
            ?? throw new AuthorizationException("not_signed_in", "driver.config", []);
        return new(
            await EnrolmentRules.PositiveSetting(settings, "driver.pin_length", null, cancellationToken),
            await EnrolmentRules.PositiveSetting(settings, "driver.max_pin_tries", null, cancellationToken),
            await EnrolmentRules.PositiveSetting(settings, "driver.max_offline_days", null, cancellationToken),
            options.Value.OfficePhone ?? throw new InvalidOperationException("Office phone is not configured."),
            employee.FirstName) { EmployeeStatus = employee.EmployeeStatus, PinResetRequired = session.SessionId is Guid sessionId && await access.PinResetRequiredAsync(sessionId, cancellationToken) };
    }
}
