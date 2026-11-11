using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
namespace FleetOps.Modules.Maintenance;

public sealed class RepairCommands(IRepairStore store, IDefectStore defects, IOfficeDefectStore office,
    AssignmentChanges assignments, IChangedSubjects changed, ICompanyTimeService companyTime, TimeProvider time, IMaintenanceDocuments documents) :
    ICommandHandler<CreateRepairCommand, RepairDetail>, ICommandHandler<StartRepairCommand, RepairDetail>,
    ICommandHandler<RescheduleMaintenanceCommand, RepairDetail>, ICommandHandler<DoneRepairCommand, RepairDetail>, ICommandHandler<CancelRepairCommand, RepairDetail>
{
    public async Task<RepairDetail> HandleAsync(CreateRepairCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var fields = new List<string>();
        if (command.VehicleId == Guid.Empty) fields.Add("vehicleId");
        if (string.IsNullOrWhiteSpace(command.Workshop) || command.Workshop.Length > 500) fields.Add("workshop");
        if (command.ScheduledFor == default) fields.Add("scheduledFor");
        if (!MaintenanceRules.Types.Contains(command.Type)) fields.Add("type");
        var dueDate = command.DueDate ?? (command.Type == "repair" && command.DefectReportIds is { Length: > 0 } && command.DueKm is null ? command.ScheduledFor : null);
        if (dueDate == default(DateOnly) || command.DueKm < 0 || dueDate is null && command.DueKm is null) fields.Add("dueDate");
        var defectIds = command.DefectReportIds ?? [];
        if (defectIds.Length > 100 || defectIds.Any(id => id == Guid.Empty) || defectIds.Distinct().Count() != defectIds.Length) fields.Add("defectReportIds");
        if (fields.Count > 0) throw new VehicleValidationException(fields);
        await store.LockAsync(context, cancellationToken);
        await defects.LockVehicleAsync(command.VehicleId, context, cancellationToken);
        await ValidateVehicleAsync(command.VehicleId, command.DueKm, context, cancellationToken);
        var reports = new List<DefectReport>();
        foreach (var id in defectIds)
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
            DefectReportIds = defectIds, Status = MaintenanceStatus.Scheduled,
            Type = command.Type, DueDate = dueDate, DueKm = command.DueKm
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

    public async Task<RepairDetail> HandleAsync(RescheduleMaintenanceCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (command.ScheduledFor == default || string.IsNullOrWhiteSpace(command.Workshop) || command.Workshop.Length > 500)
            throw new VehicleValidationException(["scheduledFor", "workshop"]);
        if (command.DueDate == default(DateOnly) || command.DueKm < 0 || command.DueDate is null && command.DueKm is null)
            throw new VehicleValidationException(["dueDate", "dueKm"]);
        var repair = await LoadAsync(command.Id, context, cancellationToken);
        if (repair.Status is not (MaintenanceStatus.Scheduled or MaintenanceStatus.InProgress)) throw new VehicleConflictException("invalid_repair_state");
        await ValidateVehicleAsync(repair.VehicleId, command.DueKm, context, cancellationToken);
        repair.ScheduledFor = command.ScheduledFor;
        repair.Workshop = command.Workshop.Trim();
        repair.DueDate = command.DueDate;
        repair.DueKm = command.DueKm;
        return await SaveAsync(repair, actor, context, cancellationToken);
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
        if (command.CostEur < 0 || command.CostEur > 999999999999.99m || command.CostEur.HasValue && decimal.Round(command.CostEur.Value, 2) != command.CostEur)
            throw new VehicleValidationException(["costEur"]);
        var repair = await LoadAsync(command.Id, context, cancellationToken);
        if (repair.Status is not (MaintenanceStatus.Scheduled or MaintenanceStatus.InProgress))
            throw new VehicleConflictException("invalid_repair_state");
        if (command.InvoiceDocumentId is Guid invoiceId) await store.ValidateInvoiceAsync(invoiceId, repair.VehicleId, context, cancellationToken);
        repair.CostEur = command.CostEur;
        repair.InvoiceDocumentId = command.InvoiceDocumentId;
        if (command.ResultDocument is not null)
            repair.ResultDocumentId = await documents.CreateVerifiedResultAsync(repair.VehicleId, command.ResultDocument, actor, context, cancellationToken);
        var swaps = await store.LinkedAssignmentsAsync(repair.Id, context, cancellationToken);
        if (swaps.Any(swap => swap.From > today)) throw new VehicleValidationException(["doneAt"]);
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
            defect.CloseReason = "Fixed in maintenance";
            await SaveDefectAsync(defect, actor, "Fixed in maintenance", context, cancellationToken);
        }
        foreach (var swap in swaps)
        {
            var recipients = await assignments.RecipientsAsync(swap, context, cancellationToken);
            swap.To = today;
            await assignments.SaveAsync(swap, actor, recipients, context, cancellationToken);
        }
        var result = await SaveAsync(repair, actor, context, cancellationToken);
        if (repair.Type != "service") return result;
        var vehicle = await store.LoadVehicleAsync(repair.VehicleId, context, cancellationToken) ?? throw new VehicleConflictException("not_found");
        if (vehicle.ServiceIntervalMonths > (9999 - doneAt.Year) * 12 + 12 - doneAt.Month) throw new VehicleValidationException(["serviceIntervalMonths"]);
        if (vehicle.ServiceIntervalKm.HasValue && command.OdometerAtDoneKm.HasValue && (long)vehicle.ServiceIntervalKm.Value + command.OdometerAtDoneKm.Value > int.MaxValue)
            throw new VehicleValidationException(["odometerAtDoneKm"]);
        DateOnly? nextDate = vehicle.ServiceIntervalMonths is > 0 ? doneAt.AddMonths(vehicle.ServiceIntervalMonths.Value) : null;
        int? nextKm = vehicle.ServiceIntervalKm is > 0 && command.OdometerAtDoneKm.HasValue
            ? checked(command.OdometerAtDoneKm.Value + vehicle.ServiceIntervalKm.Value) : null;
        return result with { NextService = nextDate.HasValue || nextKm.HasValue ? new(repair.VehicleId, "service", nextDate, nextKm) : null };
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
            defect.Status = DefectStatus.Open;
            defect.RepairId = null;
            await SaveDefectAsync(defect, actor, repair.CancelReason, context, cancellationToken);
        }
        return await SaveAsync(repair, actor, context, cancellationToken);
    }

    private async Task ValidateVehicleAsync(Guid id, int? dueKm, CommandContext context, CancellationToken cancellationToken)
    {
        var vehicle = await store.LoadVehicleAsync(id, context, cancellationToken);
        if (vehicle is null || vehicle.Status != VehicleStatus.Active) throw new VehicleValidationException(["vehicleId"]);
        if (dueKm.HasValue && vehicle.Kind != VehicleKind.Truck) throw new VehicleValidationException(["dueKm"]);
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
