using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record PersonFields(string? FirstName, string? LastName, string? Phone, string? PersonnelNumber,
    string? Email, DateOnly StartDate, string? EmploymentType, string? LanguagePref = "de",
    bool AdrRequired = false, decimal? VacationEntitlementDays = null, decimal? VacationOpeningBalance = null);
