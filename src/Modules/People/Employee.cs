namespace FleetOps.People;

public enum EmployeeStatus { Active, Suspended, Left }

public sealed class Employee
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Roles { get; set; } = "[]";
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
