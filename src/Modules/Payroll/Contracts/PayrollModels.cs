using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Payroll;

[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PayrollShiftVersion(Guid ShiftId, int Version);
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PayrollDay(Guid EmployeeId, string? PersonnelNumber, string Name, DateOnly Date,
    decimal DrivingHours, decimal OtherWorkHours, decimal AvailabilityHours,
    IReadOnlyDictionary<string, decimal> Nights, IReadOnlyDictionary<string, decimal> Absences, bool Sunday, bool Holiday);
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PayrollPerson(Guid EmployeeId, string Name, string? PersonnelNumber, int Approved, int Waiting,
    IReadOnlyList<DateOnly> Missing, bool FinalMonth, bool Ready);
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PayrollReadiness(DateOnly Month, IReadOnlyList<PayrollPerson> People) : IReadModel;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PayrollTotal(Guid EmployeeId, string Name, decimal DrivingHours, decimal OtherWorkHours,
    decimal AvailabilityHours, IReadOnlyDictionary<string, decimal> Nights, IReadOnlyDictionary<string, decimal> Absences);
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PayrollPreview(DateOnly Month, IReadOnlyList<PayrollShiftVersion> Fingerprint,
    IReadOnlyList<PayrollDay> Days, IReadOnlyList<PayrollTotal> Totals) : IReadModel;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PayrollDelta(Guid EmployeeId, DateOnly Date, PayrollDay? Old, PayrollDay? New);
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PayrollExportView(Guid Id, DateOnly PeriodFrom, DateOnly PeriodTo, string Format, string Kind,
    string Status, int Version, Guid CreatedBy, DateTimeOffset CreatedAt, DateOnly? SentDate, string? Note,
    string? Reason, Guid? CorrectionOf, string Checksum, IReadOnlyList<PayrollShiftVersion> Shifts, IReadOnlyList<PayrollDelta> Deltas) : IReadModel;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PayrollHistory(IReadOnlyList<PayrollExportView> Exports) : IReadModel;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PayrollDownload(string Url, DateTimeOffset ExpiresAt) : IReadModel;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PayrollReopened(Guid ShiftId, int Version, string Status) : IReadModel;

public sealed class PayrollValidationException(params string[] fields) : Exception("validation_error")
{
    public IReadOnlyList<string> Fields { get; } = fields;
}
public sealed class PayrollConflictException(string code, IReadOnlyList<PayrollShiftVersion>? shifts = null) : Exception(code)
{
    public string Code { get; } = code;
    public IReadOnlyList<PayrollShiftVersion> Shifts { get; } = shifts ?? [];
}
