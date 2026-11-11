using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
namespace FleetOps.Modules.Maintenance;

public sealed class RepairCommands(IRepairStore store, IDefectStore defects, IOfficeDefectStore office,
    AssignmentChanges assignments, IChangedSubjects changed, ICompanyTimeService companyTime, TimeProvider time) :
    ICommandHandler<CreateRepairCommand, RepairDetail>, ICommandHandler<StartRepairCommand, RepairDetail>,
    ICommandHandler<DoneRepairCommand, RepairDetail>, ICommandHandler<CancelRepairCommand, RepairDetail>
{
    public async Task<RepairDetail> HandleAsync(CreateRepairCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var fields = new List<string>();
        if (command.VehicleId == Guid.Empty) fields.Add("vehicleId");
        if (string.IsNullOrWhiteSpace(command.Workshop) || command.Workshop.Length > 500) fields.Add("workshop");
        if (command.ScheduledFor == default) fields.Add("scheduledFor");
        if (command.DefectReportIds is not { Length: > 0 and <= 100 } ||
            command.DefectReportIds.Any(id => id == Guid.Empty) ||
            command.DefectReportIds.Distinct().Count() != command.DefectReportIds.Length) fields.Add("defectReportIds");
        if (fields.Count > 0) throw new VehicleValidationException(fields);
        await store.LockAsync(context, cancellationToken);
        await defects.LockVehicleAsync(command.VehicleId, context, cancellationToken);
        var reports = new List<DefectReport>();
        foreach (var id in command.DefectReportIds!)
        {
            var defect = await defects.LoadAsync(id, context, cancellationToken);
            if (defect is null || defect.VehicleId != command.VehicleId ||
                defect.Status is not (DefectStatus.Open or DefectStatus.Acknowledged) || defect.RepairId.HasValue)
                throw new VehicleValidationException(["defectReportIds"]);
            reports.Add(defect);
        }
        var repair = new Maintenance
        {
            VehicleId = command.VehicleId, Workshop = command.Workshop!.Trim(), ScheduledFor = command.ScheduledFor,
            DefectReportIds = command.DefectReportIds!, Status = MaintenanceStatus.Scheduled
        };
        await store.SaveRepairAsync(repair, actor.EmployeeId, context, cancellationToken);
        foreach (var defect in reports)
        {
            defect.Status = DefectStatus.InRepair;
            defect.RepairId = repair.Id;
            await SaveDefectAsync(defect, actor, "repair_scheduled", context, cancellationToken);
        }
        changed.Add("vehicle", repair.VehicleId);
        return RepairViews.Detail(repair);
    }

    public async Task<RepairDetail> HandleAsync(StartRepairCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var repair = await LoadAsync(command.Id, context, cancellationToken);
        if (repair.Status != MaintenanceStatus.Scheduled) throw new VehicleConflictException("invalid_repair_state");
        repair.Status = MaintenanceStatus.InProgress;
        return await SaveAsync(repair, actor, context, cancellationToken);
    }

    public async Task<RepairDetail> HandleAsync(DoneRepairCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        var doneAt = command.DoneAt ?? today;
        if (doneAt == default || doneAt > today) throw new VehicleValidationException(["doneAt"]);
        if (command.OdometerAtDoneKm < 0) throw new VehicleValidationException(["odometerAtDoneKm"]);
        var repair = await LoadAsync(command.Id, context, cancellationToken);
        if (repair.Status is not (MaintenanceStatus.Scheduled or MaintenanceStatus.InProgress))
            throw new VehicleConflictException("invalid_repair_state");
        var swaps = await store.LinkedAssignmentsAsync(repair.Id, context, cancellationToken);
        if (swaps.Any(swap => swap.From > doneAt)) throw new VehicleValidationException(["doneAt"]);
        repair.Status = MaintenanceStatus.Done;
        repair.DoneAt = doneAt;
        repair.OdometerAtDoneKm = command.OdometerAtDoneKm;
        foreach (var id in repair.DefectReportIds)
        {
            var defect = await defects.LoadAsync(id, context, cancellationToken) ?? throw new VehicleConflictException("repair_defect_missing");
            if (defect.RepairId != repair.Id || defect.Status != DefectStatus.InRepair)
                throw new VehicleConflictException("invalid_defect_state");
            defect.Status = DefectStatus.Closed;
            defect.ClosedAt = time.GetUtcNow();
            defect.ClosedBy = actor.EmployeeId;
            defect.CloseReason = "Fixed by repair";
            await SaveDefectAsync(defect, actor, "Fixed by repair", context, cancellationToken);
        }
        foreach (var swap in swaps)
        {
            var recipients = await assignments.RecipientsAsync(swap, context, cancellationToken);
            swap.To = doneAt;
            await assignments.SaveAsync(swap, actor, recipients, context, cancellationToken);
        }
        return await SaveAsync(repair, actor, context, cancellationToken);
    }

    public async Task<RepairDetail> HandleAsync(CancelRepairCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 2000) throw new VehicleValidationException(["reason"]);
        var repair = await LoadAsync(command.Id, context, cancellationToken);
        if (repair.Status is not (MaintenanceStatus.Scheduled or MaintenanceStatus.InProgress))
            throw new VehicleConflictException("invalid_repair_state");
        repair.Status = MaintenanceStatus.Cancelled;
        repair.CancelReason = command.Reason.Trim();
        foreach (var id in repair.DefectReportIds)
        {
            var defect = await defects.LoadAsync(id, context, cancellationToken) ?? throw new VehicleConflictException("repair_defect_missing");
            if (defect.RepairId != repair.Id || defect.Status != DefectStatus.InRepair)
                throw new VehicleConflictException("invalid_defect_state");
            defect.Status = DefectStatus.Acknowledged;
            defect.RepairId = null;
            await SaveDefectAsync(defect, actor, repair.CancelReason, context, cancellationToken);
        }
        return await SaveAsync(repair, actor, context, cancellationToken);
    }

    private async Task<Maintenance> LoadAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await store.LockAsync(context, cancellationToken);
        var repair = await store.LoadRepairAsync(id, context, cancellationToken) ?? throw new VehicleConflictException("not_found");
        await defects.LockVehicleAsync(repair.VehicleId, context, cancellationToken);
        return repair;
    }
    private async Task<RepairDetail> SaveAsync(Maintenance repair, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await store.SaveRepairAsync(repair, actor.EmployeeId, context, cancellationToken);
        changed.Add("vehicle", repair.VehicleId);
        return RepairViews.Detail(repair);
    }
    private async Task SaveDefectAsync(DefectReport defect, AccessContext actor, string reason, CommandContext context, CancellationToken cancellationToken)
    {
        await office.SaveAsync(defect, actor.EmployeeId, reason, context, cancellationToken);
        changed.Add("defect", defect.Id);
    }
}
