using FleetOps.People;

namespace FleetOps.Api;

public sealed record CreatePersonRequest(string? FirstName, string? LastName, string[]? Roles, string? Phone,
    string? PersonnelNumber, string? Email, DateOnly StartDate, string? EmploymentType,
    string? LanguagePref = "de", bool AdrRequired = false, decimal? VacationEntitlementDays = null)
{
    public PersonFields Fields() => new(FirstName, LastName, Phone, PersonnelNumber, Email, StartDate,
        EmploymentType, LanguagePref, AdrRequired, VacationEntitlementDays);
}
