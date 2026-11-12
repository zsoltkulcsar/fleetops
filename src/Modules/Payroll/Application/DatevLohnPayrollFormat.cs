namespace FleetOps.Modules.Payroll;

public sealed class DatevLohnPayrollFormat : IPayrollExportFormat
{
    public string Code => "datev_lohn";
    public byte[] Write(IReadOnlyList<PayrollDay> days) => throw new PayrollConflictException("datev_layout_not_confirmed");
}
