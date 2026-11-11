namespace FleetOps.Identity;

public sealed record EnrolmentEmployee(Guid Id, string? PersonnelNumber, string? Phone, bool Active, DateOnly? EndDate, string Roles, string FirstName)
{
    public string EmployeeStatus { get; init; } = "active";
}
