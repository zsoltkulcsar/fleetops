using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace FleetOps.People;

public static class PersonRules
{
    public static readonly string[] Roles = ["driver", "dispatcher", "hr", "owner"];
    public static readonly string[] EmploymentTypes = ["full_time", "part_time", "minijob", "other"];

    public static void Validate(PersonFields fields, string[]? roles)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(fields.FirstName) || fields.FirstName.Length > 100) errors.Add("firstName");
        if (string.IsNullOrWhiteSpace(fields.LastName) || fields.LastName.Length > 100) errors.Add("lastName");
        if (fields.Phone is null || !Regex.IsMatch(fields.Phone, @"^\+[1-9][0-9]{1,14}$")) errors.Add("phone");
        if (roles is null || roles.Length == 0 || roles.Any(role => !Roles.Contains(role)) || roles.Distinct().Count() != roles.Length) errors.Add("roles");
        if (fields.PersonnelNumber?.Length > 100 || roles?.Contains("driver") == true && string.IsNullOrWhiteSpace(fields.PersonnelNumber)) errors.Add("personnelNumber");
        if (fields.Email?.Length > 320 || (!string.IsNullOrWhiteSpace(fields.Email) && !new EmailAddressAttribute().IsValid(fields.Email)) || roles?.Any(role => role != "driver") == true && string.IsNullOrWhiteSpace(fields.Email)) errors.Add("email");
        if (fields.StartDate == default) errors.Add("startDate");
        if (!EmploymentTypes.Contains(fields.EmploymentType)) errors.Add("employmentType");
        if (fields.LanguagePref is not ("de" or "en")) errors.Add("languagePref");
        if (fields.VacationEntitlementDays is < 0 or > 366) errors.Add("vacationEntitlementDays");
        if (errors.Count > 0) throw new PersonValidationException(errors);
    }

    public static IReadOnlyList<string> Required(Employee employee)
    {
        var types = new List<string> { "id_card", "employment_contract" };
        if (System.Text.Json.JsonSerializer.Deserialize<string[]>(employee.Roles)?.Contains("driver") == true)
        {
            types.AddRange(["licence_c_ce", "driver_card", "code95", "medical"]);
            if (employee.AdrRequired) types.Add("adr_certificate");
        }
        return types;
    }

    public static IReadOnlyList<ChecklistItem> Checklist(PersonFacts facts, DateOnly today, bool mask)
    {
        return Required(facts.Employee).Select(type =>
        {
            if (mask && type == "medical") return new ChecklistItem(type, null, true, null);
            var candidates = facts.Documents.Where(document => document.Type == type && document.Status != "replaced");
            var current = candidates.OrderByDescending(document => document.Status == "verified")
                .ThenByDescending(document => document.CreatedAt).ThenByDescending(document => document.Version).FirstOrDefault();
            var state = current is null ? "missing" : current.Status == "verified"
                ? current.ExpiresAt < today ? "expired" : current.ExpiresAt <= today.AddDays(30) ? "expiring" : "verified"
                : current.Status;
            return new ChecklistItem(type, state, false, current?.ExpiresAt);
        }).ToArray();
    }

    public static bool Valid(ChecklistItem item) => item.State is "verified" or "expiring";
    public static bool Blocking(string type) => type is "licence_c_ce" or "driver_card" or "code95" or "medical";
    public static string Status(Employee employee) => employee.Status.ToString().ToLowerInvariant();
    public static PersonFields Fields(Employee employee) => new(employee.FirstName, employee.LastName, employee.Phone,
        employee.PersonnelNumber, employee.Email, employee.StartDate, employee.EmploymentType, employee.LanguagePref,
        employee.AdrRequired, employee.VacationEntitlementDays);

    public static PersonRow Row(PersonFacts facts, DateOnly today, bool mask)
    {
        var checklist = Checklist(facts, today, mask).Where(item => !item.Masked).ToArray();
        var worst = new[] { "missing", "expired", "rejected", "uploaded", "expiring", "verified" }.FirstOrDefault(state => checklist.Any(item => item.State == state));
        var reason = facts.ReasonCategories.FirstOrDefault(category => category != "medical");
        if (reason is null && facts.ReasonCategories.Contains("medical")) reason = mask ? "document" : "medical";
        var employee = facts.Employee;
        if (!facts.CanDrive && reason is null && employee.Status != EmployeeStatus.Active) reason = Status(employee);
        return new(employee.Id, employee.FirstName + " " + employee.LastName,
            System.Text.Json.JsonSerializer.Deserialize<string[]>(employee.Roles) ?? [], Status(employee), facts.CanDrive,
            reason, checklist.Count(Valid), checklist.Length, worst, facts.DefaultVehiclePlate, employee.Phone);
    }
}
