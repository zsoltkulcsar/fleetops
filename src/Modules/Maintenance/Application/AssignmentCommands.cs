using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
namespace FleetOps.Modules.Maintenance;

public sealed class AssignmentCommands(IRepairStore store, IDefectStore defects, AssignmentChanges changes,
    ICompanyTimeService companyTime, TimeProvider time) :
    ICommandHandler<CreateAssignmentCommand, AssignmentDetail>, ICommandHandler<EditAssignmentCommand, AssignmentDetail>,
    ICommandHandler<EndAssignmentCommand, AssignmentDetail>
{
    private DateOnly Today => companyTime.ToCompanyDate(time.GetUtcNow());

    public async Task<AssignmentDetail> HandleAsync(CreateAssignmentCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await store.LockAsync(context, cancellationToken);
        var assignment = new Assignment
        {
            EmployeeId = command.EmployeeId, VehicleId = command.VehicleId, From = Today,
            To = command.To, MaintenanceId = command.MaintenanceId
        };
        if (command.MaintenanceId.HasValue)
        {
            var repair = await store.LoadRepairAsync(command.MaintenanceId.Value, context, cancellationToken);
            if (repair is null || repair.Status is MaintenanceStatus.Done or MaintenanceStatus.Cancelled)
                throw new VehicleValidationException(["maintenanceId"]);
            assignment.To = repair.ScheduledFor;
        }
        await ValidateAsync(assignment, context, cancellationToken);
        await changes.SaveAsync(assignment, actor, [], context, cancellationToken);
        return RepairViews.Detail(assignment);
    }

    public async Task<AssignmentDetail> HandleAsync(EditAssignmentCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var assignment = await LoadAsync(command.Id, context, cancellationToken);
        await EnsureRepairOpenAsync(assignment, context, cancellationToken);
        var recipients = await changes.RecipientsAsync(assignment, context, cancellationToken);
        assignment.From = command.From;
        assignment.To = command.To;
        await ValidateAsync(assignment, context, cancellationToken);
        await changes.SaveAsync(assignment, actor, recipients, context, cancellationToken);
        return RepairViews.Detail(assignment);
    }

    public async Task<AssignmentDetail> HandleAsync(EndAssignmentCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var assignment = await LoadAsync(command.Id, context, cancellationToken);
        if (assignment.From > Today || assignment.To < Today) throw new VehicleConflictException("invalid_assignment_state");
        var recipients = await changes.RecipientsAsync(assignment, context, cancellationToken);
        assignment.To = Today;
        await changes.SaveAsync(assignment, actor, recipients, context, cancellationToken);
        return RepairViews.Detail(assignment);
    }

    private async Task<Assignment> LoadAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await store.LockAsync(context, cancellationToken);
        return await store.LoadAssignmentAsync(id, context, cancellationToken) ?? throw new VehicleConflictException("not_found");
    }
    private async Task EnsureRepairOpenAsync(Assignment assignment, CommandContext context, CancellationToken cancellationToken)
    {
        if (assignment.MaintenanceId is not Guid repairId) return;
        var repair = await store.LoadRepairAsync(repairId, context, cancellationToken);
        if (repair is null || repair.Status == MaintenanceStatus.Done) throw new VehicleConflictException("invalid_repair_state");
    }
    private async Task ValidateAsync(Assignment assignment, CommandContext context, CancellationToken cancellationToken)
    {
        if (assignment.From == default || assignment.To == default(DateOnly) || assignment.To < assignment.From)
            throw new VehicleValidationException(["from", "to"]);
        if (!await store.IsActiveDriverAsync(assignment.EmployeeId, assignment.From, context, cancellationToken))
            throw new VehicleValidationException(["employeeId"]);
        if (assignment.VehicleId == Guid.Empty) throw new VehicleValidationException(["vehicleId"]);
        await defects.LockVehicleAsync(assignment.VehicleId, context, cancellationToken);
        if (!await store.IsUsableTruckAsync(assignment.VehicleId, assignment.From, context, cancellationToken))
            throw new VehicleConflictException("vehicle_unusable");
    }
}
