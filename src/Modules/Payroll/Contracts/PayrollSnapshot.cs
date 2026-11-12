namespace FleetOps.Modules.Payroll;

public sealed record PayrollSnapshot(IReadOnlyList<PayrollShiftFact> Shifts, IReadOnlyList<PayrollDay> Absences);
