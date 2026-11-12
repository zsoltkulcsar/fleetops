using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Payroll;

[QueryPolicy("payroll.export")]
public sealed record GetPayrollReadinessQuery(DateOnly Month) : IQuery<PayrollReadiness>;
[QueryPolicy("payroll.export")]
public sealed record PreviewPayrollQuery(DateOnly Month) : IQuery<PayrollPreview>;
[QueryPolicy("payroll.export")]
public sealed record GetPayrollHistoryQuery(DateOnly Month) : IQuery<PayrollHistory>;
[CommandPolicy("payroll.export", RequiresStepUp = true)]
public sealed record GeneratePayrollCommand(DateOnly Month, string Format, IReadOnlyList<PayrollShiftVersion> Fingerprint) : ICommand<PayrollExportView>;
[CommandPolicy("payroll.export")]
public sealed record MarkPayrollSentCommand(Guid ExportId, DateOnly SentDate, string? Note) : ICommand<PayrollExportView>;
[CommandPolicy("payroll.export")]
public sealed record DiscardPayrollCommand(Guid ExportId, string Reason) : ICommand<PayrollExportView>;
[CommandPolicy("shifts.reopen_exported")]
public sealed record ReopenPayrollShiftCommand(Guid ShiftId, int Version, string Reason) : ICommand<PayrollReopened>;
// Link creation writes its audit record in the command transaction.
[CommandPolicy("payroll.export")]
public sealed record DownloadPayrollCommand(Guid ExportId) : ICommand<PayrollDownload>;

public interface IPayrollExportFormat
{
    string Code { get; }
    byte[] Write(IReadOnlyList<PayrollDay> days);
}

public sealed record PayrollShiftFact(Guid ShiftId, int Version, string Status, Guid? ExportId, IReadOnlyList<PayrollDay> Days);
public sealed record PayrollMonthFacts(IReadOnlyList<PayrollPerson> People, IReadOnlyList<PayrollShiftFact> Shifts, IReadOnlyList<PayrollDay> Absences);
public interface IPayrollStore
{
    Task<PayrollMonthFacts> ReadAsync(DateOnly month, CommandContext? context, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayrollExport>> HistoryAsync(DateOnly month, CommandContext? context, CancellationToken cancellationToken);
    Task LockAsync(CommandContext context, CancellationToken cancellationToken);
    Task<PayrollExport?> FindAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
    Task SaveExportAsync(PayrollExport export, Guid actorId, string reason, CommandContext context, CancellationToken cancellationToken);
    Task SetShiftsAsync(IReadOnlyList<PayrollShiftVersion> shifts, string status, Guid? exportId, Guid actorId, string reason, CommandContext context, CancellationToken cancellationToken);
    Task<(Guid ExportId, int Version)?> ExportedShiftAsync(Guid shiftId, CommandContext context, CancellationToken cancellationToken);
    Task AuditDownloadAsync(Guid exportId, Guid actorId, CommandContext context, CancellationToken cancellationToken);
}
