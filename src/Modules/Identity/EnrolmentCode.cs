namespace FleetOps.Identity;
public sealed class EnrolmentCode
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public Guid IssuedBy { get; set; }
}
