using FleetOps.People;

namespace FleetOps.Api;

public sealed record EditPersonRequest(string? FirstName, string? LastName, string? Phone, string? PersonnelNumber,
    string? Email, DateOnly StartDate, string? EmploymentType, string? LanguagePref = "de",
    bool AdrRequired = false, decimal? VacationEntitlementDays = null, decimal? VacationOpeningBalance = null)
{
    public PersonFields Fields() => new(FirstName, LastName, Phone, PersonnelNumber, Email, StartDate,
        EmploymentType, LanguagePref, AdrRequired, VacationEntitlementDays, VacationOpeningBalance);
}
