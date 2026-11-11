namespace FleetOps.People;

public enum EmployeeStatus { Active = 0, Suspended = 1, Left = 2, Onboarding = 3 }

public sealed class Employee
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? PersonnelNumber { get; set; }
    public string? Phone { get; set; }
    public string Email { get; set; } = "";
    public string Roles { get; set; } = "[]";
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;
    public Guid? DepotId { get; set; }
    public string EmploymentType { get; set; } = "full_time";
    public string LanguagePref { get; set; } = "de";
    public bool AdrRequired { get; set; }
    public decimal? VacationEntitlementDays { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public CyclePattern? CyclePattern { get; set; }
    public int CyclePatternVersion { get; set; }
}
