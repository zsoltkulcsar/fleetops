using System.Security.Cryptography;
using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;

namespace FleetOps.Modules.Payroll;

public sealed class PayrollCommands(IPayrollStore store, IEnumerable<IPayrollExportFormat> formats, IObjectStore objects, TimeProvider clock) :
    ICommandHandler<GeneratePayrollCommand, PayrollExportView>, ICommandHandler<MarkPayrollSentCommand, PayrollExportView>,
    ICommandHandler<DiscardPayrollCommand, PayrollExportView>, ICommandHandler<ReopenPayrollShiftCommand, PayrollReopened>,
    ICommandHandler<DownloadPayrollCommand, PayrollDownload>
{
    public async Task<PayrollExportView> HandleAsync(GeneratePayrollCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        PayrollViews.ValidateMonth(command.Month);
        if (command.Fingerprint is null || command.Fingerprint.Count > 100000 || command.Fingerprint.Any(item => item is null || item.ShiftId == Guid.Empty || item.Version < 1) ||
            command.Fingerprint.Select(item => item.ShiftId).Distinct().Count() != command.Fingerprint.Count) throw new PayrollValidationException("fingerprint");
        var format = formats.SingleOrDefault(format => format.Code == command.Format) ?? throw new PayrollValidationException("format");
        // Refuse an unimplemented format before taking locks or writing any state.
        format.Write([]);
        await store.LockAsync(context, cancellationToken);
        var facts = await store.ReadAsync(command.Month, context, cancellationToken);
        var preview = PayrollViews.Preview(command.Month, facts);
        var expected = command.Fingerprint.ToDictionary(item => item.ShiftId, item => item.Version);
        var actual = preview.Fingerprint.ToDictionary(item => item.ShiftId, item => item.Version);
        var changed = expected.Keys.Union(actual.Keys).Where(id => !expected.TryGetValue(id, out var oldVersion) || !actual.TryGetValue(id, out var newVersion) || oldVersion != newVersion)
            .Select(id => new PayrollShiftVersion(id, facts.Shifts.SingleOrDefault(shift => shift.ShiftId == id)?.Version ?? 0)).OrderBy(item => item.ShiftId).ToArray();
        if (changed.Length > 0) throw new PayrollConflictException("export_changed", changed);
        var history = await store.HistoryAsync(command.Month, context, cancellationToken);
        if (history.Any(export => export.Status == "generated")) throw new PayrollConflictException("export_already_generated");
        var sent = history.Where(export => export.Status == "sent").OrderBy(export => export.Version).ToArray();
        var approved = facts.Shifts.Where(shift => actual.ContainsKey(shift.ShiftId)).ToArray();
        var baselines = new Dictionary<Guid, PayrollShiftFact>();
        foreach (var prior in sent)
            foreach (var shift in JsonSerializer.Deserialize<PayrollSnapshot>(prior.SnapshotJson)!.Shifts) baselines[shift.ShiftId] = shift;
        var oldDays = PayrollViews.Combine(approved.Where(shift => baselines.ContainsKey(shift.ShiftId)).SelectMany(shift => baselines[shift.ShiftId].Days)
            .Concat(sent.Length == 0 ? [] : JsonSerializer.Deserialize<PayrollSnapshot>(sent[^1].SnapshotJson)!.Absences));
        var newDays = preview.Days;
        var deltas = oldDays.Select(day => (day.EmployeeId, day.Date)).Union(newDays.Select(day => (day.EmployeeId, day.Date)))
            .Select(key => new PayrollDelta(key.EmployeeId, key.Date, oldDays.SingleOrDefault(day => day.EmployeeId == key.EmployeeId && day.Date == key.Date),
                newDays.SingleOrDefault(day => day.EmployeeId == key.EmployeeId && day.Date == key.Date)))
            .Where(delta => !Equivalent(delta.Old, delta.New)).ToArray();
        if (approved.Length == 0 && deltas.Length == 0) throw new PayrollConflictException("nothing_to_export");
        var corrected = approved.FirstOrDefault(shift => baselines.ContainsKey(shift.ShiftId));
        var priorAbsences = sent.Length == 0 ? [] : JsonSerializer.Deserialize<PayrollSnapshot>(sent[^1].SnapshotJson)!.Absences;
        var absenceChanged = priorAbsences.Count != facts.Absences.Count || priorAbsences.Any(prior => !Equivalent(prior, facts.Absences.SingleOrDefault(day => day.EmployeeId == prior.EmployeeId && day.Date == prior.Date)));
        var kind = sent.Length == 0 ? "regular" : corrected is null && !absenceChanged ? "supplement" : "correction";
        var export = new PayrollExport
        {
            PeriodFrom = command.Month,
            PeriodTo = command.Month.AddMonths(1).AddDays(-1),
            Format = format.Code,
            Kind = kind,
            Version = history.Select(item => item.Version).DefaultIfEmpty().Max() + 1,
            CreatedBy = actor.EmployeeId,
            CreatedAt = clock.GetUtcNow(),
            CorrectionOf = kind == "correction" ? sent.LastOrDefault(item => corrected is null || JsonSerializer.Deserialize<PayrollSnapshot>(item.SnapshotJson)!.Shifts.Any(shift => shift.ShiftId == corrected.ShiftId))?.Id : null,
            ShiftVersionsJson = JsonSerializer.Serialize(preview.Fingerprint),
            SnapshotJson = JsonSerializer.Serialize(new PayrollSnapshot(approved, facts.Absences)),
            DeltasJson = JsonSerializer.Serialize(kind == "correction" ? deltas : [])
        };
        var output = sent.Length == 0 ? newDays : deltas.Select(Difference).ToArray();
        var bytes = format.Write(output);
        export.Checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));
        export.FileKey = $"payroll/{export.Id:N}/{command.Month:yyyy-MM}-{kind}-v{export.Version}.csv";
        await objects.WriteAsync(export.FileKey, bytes, "text/csv; charset=utf-8", cancellationToken);
        try
        {
            await store.SaveExportAsync(export, actor.EmployeeId, "generate", context, cancellationToken);
            await store.SetShiftsAsync(preview.Fingerprint, "Exported", export.Id, actor.EmployeeId, "payroll_export", context, cancellationToken);
        }
        catch
        {
            // The command dispatcher rolls back all database writes.
            try { await objects.DeleteAsync(export.FileKey, CancellationToken.None); }
            catch { /* An unavailable object store must not hide the original refusal. */ }
            throw;
        }
        return PayrollViews.From(export);
    }

    public async Task<PayrollExportView> HandleAsync(MarkPayrollSentCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (command.SentDate == default || command.SentDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) || command.Note?.Length > 500) throw new PayrollValidationException("sentDate", "note");
        var export = await LoadAsync(command.ExportId, context, cancellationToken);
        if (export.Status != "generated") throw new PayrollConflictException("export_not_generated");
        export.Status = "sent";
        export.SentDate = command.SentDate;
        export.SentBy = actor.EmployeeId;
        export.Note = command.Note?.Trim();
        await store.SaveExportAsync(export, actor.EmployeeId, "mark_sent", context, cancellationToken);
        return PayrollViews.From(export);
    }

    public async Task<PayrollExportView> HandleAsync(DiscardPayrollCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        RequireReason(command.Reason);
        var export = await LoadAsync(command.ExportId, context, cancellationToken);
        if (export.Status != "generated") throw new PayrollConflictException("export_not_generated");
        await UnlockAsync(export, actor.EmployeeId, command.Reason.Trim(), context, cancellationToken);
        export.Status = "discarded";
        export.Reason = command.Reason.Trim();
        await store.SaveExportAsync(export, actor.EmployeeId, command.Reason.Trim(), context, cancellationToken);
        return PayrollViews.From(export);
    }

    public async Task<PayrollReopened> HandleAsync(ReopenPayrollShiftCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        RequireReason(command.Reason);
        await store.LockAsync(context, cancellationToken);
        var shift = await store.ExportedShiftAsync(command.ShiftId, context, cancellationToken) ?? throw new PayrollConflictException("shift_not_exported");
        if (shift.Version != command.Version) throw new PayrollConflictException("export_changed", [new(command.ShiftId, shift.Version)]);
        var export = await store.FindAsync(shift.ExportId, context, cancellationToken) ?? throw new PayrollConflictException("not_found");
        if (export.Status == "generated")
        {
            await UnlockAsync(export, actor.EmployeeId, command.Reason.Trim(), context, cancellationToken, command.ShiftId);
            export.Status = "superseded";
            export.Reason = command.Reason.Trim();
            await store.SaveExportAsync(export, actor.EmployeeId, command.Reason.Trim(), context, cancellationToken);
        }
        else if (export.Status != "sent") throw new PayrollConflictException("export_not_generated");
        await store.SetShiftsAsync([new(command.ShiftId, command.Version)], "Submitted", null, actor.EmployeeId, command.Reason.Trim(), context, cancellationToken);
        return new(command.ShiftId, command.Version + 1, "submitted");
    }

    public async Task<PayrollDownload> HandleAsync(DownloadPayrollCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var export = await LoadAsync(command.ExportId, context, cancellationToken);
        var link = await objects.CreateDownloadLinkAsync(export.FileKey, TimeSpan.FromMinutes(5), cancellationToken);
        await store.AuditDownloadAsync(export.Id, actor.EmployeeId, context, cancellationToken);
        return new(link.Url, link.ExpiresAt);
    }

    private async Task<PayrollExport> LoadAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new PayrollValidationException("exportId");
        await store.LockAsync(context, cancellationToken);
        return await store.FindAsync(id, context, cancellationToken) ?? throw new PayrollConflictException("not_found");
    }

    private Task UnlockAsync(PayrollExport export, Guid actor, string reason, CommandContext context, CancellationToken cancellationToken, Guid? exceptShiftId = null) =>
        store.SetShiftsAsync(JsonSerializer.Deserialize<PayrollShiftVersion[]>(export.ShiftVersionsJson)!.Where(shift => shift.ShiftId != exceptShiftId).Select(shift => shift with { Version = shift.Version + 1 }).ToArray(),
            "Approved", null, actor, reason, context, cancellationToken);

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new PayrollValidationException("reason");
    }

    private static PayrollDay Difference(PayrollDelta delta)
    {
        var current = delta.New ?? delta.Old!;
        return current with
        {
            DrivingHours = (delta.New?.DrivingHours ?? 0) - (delta.Old?.DrivingHours ?? 0),
            OtherWorkHours = (delta.New?.OtherWorkHours ?? 0) - (delta.Old?.OtherWorkHours ?? 0),
            AvailabilityHours = (delta.New?.AvailabilityHours ?? 0) - (delta.Old?.AvailabilityHours ?? 0),
            Nights = Subtract(delta.New?.Nights, delta.Old?.Nights),
            Absences = Subtract(delta.New?.Absences, delta.Old?.Absences)
        };
    }

    private static IReadOnlyDictionary<string, decimal> Subtract(IReadOnlyDictionary<string, decimal>? current, IReadOnlyDictionary<string, decimal>? prior) =>
        (current?.Keys ?? []).Union(prior?.Keys ?? []).ToDictionary(key => key, key => (current?.GetValueOrDefault(key) ?? 0) - (prior?.GetValueOrDefault(key) ?? 0));

    private static bool Equivalent(PayrollDay? prior, PayrollDay? current) =>
        prior is null ? current is null : current is not null && prior.EmployeeId == current.EmployeeId && prior.Date == current.Date &&
        prior.DrivingHours == current.DrivingHours && prior.OtherWorkHours == current.OtherWorkHours && prior.AvailabilityHours == current.AvailabilityHours &&
        prior.Sunday == current.Sunday && prior.Holiday == current.Holiday && SameCounts(prior.Nights, current.Nights) && SameCounts(prior.Absences, current.Absences);

    private static bool SameCounts(IReadOnlyDictionary<string, decimal> prior, IReadOnlyDictionary<string, decimal> current) =>
        prior.Count == current.Count && prior.All(item => current.TryGetValue(item.Key, out var value) && value == item.Value);
}
