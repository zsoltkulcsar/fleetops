namespace FleetOps.Modules.Payroll;

public sealed class PayrollExport
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public string Format { get; set; } = "csv_generic";
    public string Kind { get; set; } = "regular";
    public string Status { get; set; } = "generated";
    public int Version { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? SentBy { get; set; }
    public DateOnly? SentDate { get; set; }
    public string? Note { get; set; }
    public string? Reason { get; set; }
    public Guid? CorrectionOf { get; set; }
    public string ShiftVersionsJson { get; set; } = "[]";
    public string SnapshotJson { get; set; } = "[]";
    public string DeltasJson { get; set; } = "[]";
    public string FileKey { get; set; } = "";
    public string Checksum { get; set; } = "";
}
