using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Shifts;

public sealed class OfficeShiftHandler(IOfficeShiftStore store, IShiftCommandStore driverStore, IShiftStartFacts startFacts,
    IShiftEvents events, ICompanyTimeService companyTime, TimeProvider clock) :
    ICommandHandler<ApproveShiftCommand, OfficeShiftResult>, ICommandHandler<RejectShiftCommand, OfficeShiftResult>,
    ICommandHandler<EditOfficeShiftCommand, OfficeShiftResult>, ICommandHandler<AddOfficeShiftCommand, OfficeShiftResult>,
    ICommandHandler<EndOfficeShiftCommand, OfficeShiftResult>
{
    public async Task<OfficeShiftResult> HandleAsync(ApproveShiftCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var shift = await LoadAsync(command.SubjectId, command.ShiftId, command.Version, actor, context, cancellationToken);
        if (shift.Status != ShiftStatus.Submitted) throw new ShiftRuleException("not_submitted");
        if (command.Note?.Length > 500) throw new TimesheetValidationException("note");
        if (await store.IsFlaggedAsync(shift, context, cancellationToken) && string.IsNullOrWhiteSpace(command.Note))
            throw new ShiftRuleException("note_required");
        shift.Status = ShiftStatus.Approved;
        shift.ApprovedBy = actor.EmployeeId;
        shift.ApprovedAt = clock.GetUtcNow();
        shift.ApprovalNote = command.Note?.Trim();
        return await SaveAsync(shift, actor, command.Note?.Trim() ?? "approve", null, context, cancellationToken);
    }

    public async Task<OfficeShiftResult> HandleAsync(RejectShiftCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        RequireReason(command.Reason);
        var shift = await LoadAsync(command.SubjectId, command.ShiftId, command.Version, actor, context, cancellationToken);
        if (shift.Status != ShiftStatus.Submitted) throw new ShiftRuleException("not_submitted");
        shift.Status = ShiftStatus.Rejected;
        shift.RejectReason = command.Reason.Trim();
        ClearApproval(shift);
        return await SaveAsync(shift, actor, command.Reason.Trim(), "shift_rejected", context, cancellationToken);
    }

    public async Task<OfficeShiftResult> HandleAsync(EditOfficeShiftCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        RequireReason(command.Reason);
        var shift = await LoadAsync(command.SubjectId, command.ShiftId, command.Version, actor, context, cancellationToken);
        if (shift.Status == ShiftStatus.Exported) throw new ShiftRuleException("locked");
        if (command.Input is null) throw new TimesheetValidationException("input");
        if (command.Input.StartAt != shift.StartAt || command.Input.Date != shift.Date) throw new TimesheetValidationException("startAt", "date");
        if (shift.Status == ShiftStatus.Running && command.Input.EndAt is not null) throw new ShiftRuleException("use_office_end");
        ApplyInput(shift, command.Input);
        if (shift.Status == ShiftStatus.Approved)
        {
            shift.Status = ShiftStatus.Submitted;
            shift.SubmittedAt = clock.GetUtcNow();
        }
        ClearApproval(shift);
        await store.ValidateReferencesAsync(shift, context, cancellationToken);
        return await SaveAsync(shift, actor, command.Reason.Trim(), "shift_edited_by_office", context, cancellationToken);
    }

    public async Task<OfficeShiftResult> HandleAsync(AddOfficeShiftCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        RequireOffice(actor);
        RequireReason(command.Reason);
        if (command.SubjectId is not Guid employeeId || employeeId == Guid.Empty) throw new TimesheetValidationException("employeeId");
        if (command.Input is null || command.Input.EndAt is null) throw new TimesheetValidationException("endAt");
        await driverStore.LockEmployeeAsync(employeeId, context, cancellationToken);
        var shift = ShiftRules.Start(Guid.CreateVersion7(), employeeId, command.Input.Date, command.Input.StartAt, command.Input.TruckId, command.Input.TrailerId, command.Input.OdometerStartKm);
        ApplyInput(shift, command.Input);
        shift.Source = ShiftSource.Office;
        shift.Status = ShiftStatus.Submitted;
        shift.SubmittedAt = clock.GetUtcNow();
        shift.ServerReceivedAt = clock.GetUtcNow();
        var facts = await startFacts.ReadAsync(employeeId, shift.TruckId, shift.TrailerId, shift.StartAt, context, cancellationToken);
        shift.OverrideId = shift.TruckId is null ? null : facts.OverrideId;
        if (!facts.Active || !facts.DriverCanDrive) shift.BlockedAtStart = "driver_blocked";
        else if (shift.TruckId is not null && !facts.TruckUsable) shift.BlockedAtStart = "truck_unusable";
        await store.ValidateReferencesAsync(shift, context, cancellationToken);
        return await SaveAsync(shift, actor, command.Reason.Trim(), "shift_edited_by_office", context, cancellationToken);
    }

    public async Task<OfficeShiftResult> HandleAsync(EndOfficeShiftCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        RequireReason(command.Reason);
        var shift = await LoadAsync(command.SubjectId, command.ShiftId, command.Version, actor, context, cancellationToken);
        if (command.EndAt == default || command.EndAt > clock.GetUtcNow()) throw new TimesheetValidationException("endAt");
        ShiftRules.End(shift, command.EndAt, command.OdometerEndKm);
        return await SaveAsync(shift, actor, command.Reason.Trim(), "shift_edited_by_office", context, cancellationToken);
    }

    private async Task<Shift> LoadAsync(Guid? employeeId, Guid shiftId, int version, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        RequireOffice(actor);
        if (employeeId is not Guid subject || subject == Guid.Empty || shiftId == Guid.Empty) throw new TimesheetValidationException("shiftId");
        await driverStore.LockEmployeeAsync(subject, context, cancellationToken);
        var shift = await store.LoadAsync(subject, shiftId, context, cancellationToken) ?? throw new ShiftRuleException("not_found");
        if (shift.Version != version) throw new ShiftRuleException("version_changed");
        return shift;
    }

    private async Task<OfficeShiftResult> SaveAsync(Shift shift, AccessContext actor, string reason, string? notification, CommandContext context, CancellationToken cancellationToken)
    {
        shift.Version++;
        shift.LastOfficeVersion = shift.Version;
        var auditId = await store.SaveAsync(shift, actor.EmployeeId, reason, context, cancellationToken);
        if (notification is not null) await store.NotifyAsync(shift.EmployeeId, shift.Id, auditId, notification, context, cancellationToken);
        await events.ChangedAsync(shift.EmployeeId, shift.Id, null, context, cancellationToken);
        return new(shift.Id, shift.Version, shift.Status);
    }

    private void ApplyInput(Shift shift, OfficeShiftInput input)
    {
        if (input.StartAt == default || input.StartAt > clock.GetUtcNow() || input.EndAt < input.StartAt || input.EndAt > clock.GetUtcNow() ||
            input.Date != companyTime.ToCompanyDate(input.StartAt) || (shift.Status != ShiftStatus.Running && input.EndAt is null))
            throw new TimesheetValidationException("startAt", "endAt", "date");
        if (input.OdometerStartKm < 0 || input.OdometerEndKm < 0 || input.OdometerEndKm < input.OdometerStartKm)
            throw new TimesheetValidationException("odometerStartKm", "odometerEndKm");
        if (input.TruckId == Guid.Empty || input.TrailerId == Guid.Empty || (input.TruckId is null && input.TrailerId is not null))
            throw new TimesheetValidationException("truckId", "trailerId");
        var existingActivities = shift.Activities.OrderBy(activity => activity.StartAt).ToArray();
        var activities = input.Activities?.ToArray() ?? existingActivities.Select((activity, index) =>
            new ActivityView(activity.Id, activity.Type, activity.StartAt, index == existingActivities.Length - 1 ? input.EndAt : activity.EndAt)).ToArray();
        if (activities.Length is < 1 or > 500 || activities.Any(activity => activity is null) || activities.Select(activity => activity.Id).Distinct().Count() != activities.Length)
            throw new TimesheetValidationException("activities");
        activities = activities.OrderBy(activity => activity.StartAt).ToArray();
        for (var index = 0; index < activities.Length; index++)
        {
            var activity = activities[index];
            if (activity.Id == Guid.Empty || !Enum.IsDefined(activity.Type) || activity.EndAt < activity.StartAt || activity.StartAt > clock.GetUtcNow() || activity.EndAt > clock.GetUtcNow() ||
                (index == 0 && activity.StartAt != input.StartAt) || (index == activities.Length - 1 && activity.EndAt != input.EndAt) ||
                (index < activities.Length - 1 && activity.EndAt != activities[index + 1].StartAt) ||
                (activity.Type == ActivityType.Driving && input.TruckId is null)) throw new TimesheetValidationException("activities");
        }
        var nights = input.Overnights ?? shift.Overnights.Select(night => new OvernightView(night.Id, night.Night, night.CountryCode, night.Kind)).ToArray();
        if (nights.Count > 2 || nights.Any(night => night is null) || nights.Select(night => night.Night).Distinct().Count() != nights.Count)
            throw new TimesheetValidationException("overnights");
        // Reuse the driver overnight rules on a draft, then restore the office lifecycle.
        var validationShift = new Shift { Id = shift.Id, Date = shift.Date, Status = ShiftStatus.Draft };
        foreach (var night in nights) ShiftRules.RecordNight(validationShift, night.Night, night.CountryCode, night.Kind);
        shift.TruckId = input.TruckId;
        shift.TrailerId = input.TrailerId;
        shift.EndAt = input.EndAt;
        shift.OdometerStartKm = input.OdometerStartKm;
        shift.OdometerEndKm = input.OdometerEndKm;
        shift.Activities = activities.Select(activity => new ShiftActivity { Id = activity.Id, ShiftId = shift.Id, Type = activity.Type,
            StartAt = activity.StartAt, EndAt = activity.EndAt, RecordedAt = clock.GetUtcNow() }).ToList();
        shift.Overnights = validationShift.Overnights.Select(night =>
        {
            var existing = shift.Overnights.SingleOrDefault(original => original.Night == night.Night);
            if (existing is not null) night.Id = existing.Id;
            return night;
        }).ToList();
    }

    private static void RequireOffice(AccessContext actor)
    {
        if (!actor.Roles.Any(role => role is RoleCode.Dispatcher or RoleCode.Hr or RoleCode.Owner))
            throw new AuthorizationException(RefusalCodes.PermissionDenied, "shifts.enter_edit", [RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner]);
    }
    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new TimesheetValidationException("reason");
    }
    private static void ClearApproval(Shift shift)
    {
        shift.ApprovedAt = null;
        shift.ApprovedBy = null;
        shift.ApprovalNote = null;
    }
}
