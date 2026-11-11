using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;

public sealed class OfficeDefectCommandHandler(IDefectStore defects, IOfficeDefectStore office, IDefectEvents events,
    IChangedSubjects changed, TimeProvider time) : ICommandHandler<ConfirmDefectCommand, DefectActionResult>,
    ICommandHandler<DowngradeDefectCommand, DefectActionResult>, ICommandHandler<CloseDefectCommand, DefectActionResult>,
    ICommandHandler<LogOfficeDefectCommand, DefectActionResult>
{
    public async Task<DefectActionResult> HandleAsync(ConfirmDefectCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var defect = await LoadLockedAsync(command.Id, context, cancellationToken);
        DefectRules.Confirm(defect);
        return await SaveAsync(defect, actor, "office_defect_confirm", context, cancellationToken);
    }
    public async Task<DefectActionResult> HandleAsync(DowngradeDefectCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var defect = await LoadLockedAsync(command.Id, context, cancellationToken);
        DefectRules.Downgrade(defect, command.Severity, command.Reason);
        return await SaveAsync(defect, actor, command.Reason!.Trim(), context, cancellationToken);
    }
    public async Task<DefectActionResult> HandleAsync(CloseDefectCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var defect = await LoadLockedAsync(command.Id, context, cancellationToken);
        DefectRules.Close(defect, actor.EmployeeId, time.GetUtcNow(), command.Reason, defect.RepairId.HasValue);
        return await SaveAsync(defect, actor, command.Reason!.Trim(), context, cancellationToken);
    }
    public async Task<DefectActionResult> HandleAsync(LogOfficeDefectCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var defect = new DefectReport
        {
            VehicleId = command.VehicleId, Category = command.Category, Severity = command.Severity,
            Description = command.Description, Photos = command.Photos ?? [], ReportedBy = command.ReportedBy ?? actor.EmployeeId,
            Origin = DefectOrigin.OfficeFromChat, ReportedAt = now, DeviceTime = now, ServerReceivedAt = now
        };
        DefectRules.Validate(defect);
        await defects.LockVehicleAsync(defect.VehicleId, context, cancellationToken);
        await office.ValidateReporterAsync(defect.ReportedBy, actor.EmployeeId, context, cancellationToken);
        await office.ValidatePhotosAsync(defect, actor.EmployeeId, context, cancellationToken);
        var wasOffRoad = await defects.IsOffRoadAsync(defect.VehicleId, context, cancellationToken);
        var result = await SaveAsync(defect, actor, "office_from_chat", context, cancellationToken);
        await events.ReportedAsync(defect, DefectRules.BlocksVehicle(defect) && !wasOffRoad, context, cancellationToken);
        return result;
    }
    private async Task<DefectReport> LoadLockedAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        var defect = await defects.LoadAsync(id, context, cancellationToken) ?? throw new VehicleConflictException("not_found");
        await defects.LockVehicleAsync(defect.VehicleId, context, cancellationToken);
        return await defects.LoadAsync(id, context, cancellationToken) ?? throw new VehicleConflictException("not_found");
    }
    private async Task<DefectActionResult> SaveAsync(DefectReport defect, AccessContext actor, string reason, CommandContext context, CancellationToken cancellationToken)
    {
        await office.SaveAsync(defect, actor.EmployeeId, reason, context, cancellationToken);
        changed.Add("defect", defect.Id);
        changed.Add("vehicle", defect.VehicleId);
        return new(defect.Id, defect.Status, defect.Severity);
    }
}
