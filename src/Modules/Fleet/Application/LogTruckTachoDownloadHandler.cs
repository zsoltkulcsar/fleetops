using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;

public sealed class LogTruckTachoDownloadHandler(ITruckTachoDownloads store, ICompanyTimeService companyTime, TimeProvider time, IChangedSubjects changed)
    : ICommandHandler<LogTruckTachoDownloadCommand, TruckTachoDownloadResult>
{
    public async Task<TruckTachoDownloadResult> HandleAsync(LogTruckTachoDownloadCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (command.Id == Guid.Empty || command.Date == default || command.Date > companyTime.ToCompanyDate(time.GetUtcNow()))
            throw new VehicleValidationException(["date", "id"]);
        var truck = await store.LockAsync(command.Id, context, cancellationToken);
        if (truck is null || truck.Kind != VehicleKind.Truck || truck.Status != VehicleStatus.Active)
            throw new VehicleValidationException(["id"]);
        truck.LastTachoDownloadAt = command.Date;
        await store.SaveAsync(truck, actor.EmployeeId, context, cancellationToken);
        changed.Add("vehicle", command.Id);
        return new(command.Id, command.Date);
    }
}
