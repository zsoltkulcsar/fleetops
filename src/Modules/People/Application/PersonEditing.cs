using System.Text.Json;
using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

internal static class PersonEditing
{
    internal static async Task ApplyAsync(IPeopleStore store, Employee employee, PersonFields fields, AccessContext actor,
        CommandContext context, bool create, CancellationToken cancellationToken)
    {
        var number = string.IsNullOrWhiteSpace(fields.PersonnelNumber) ? null : fields.PersonnelNumber.Trim();
        if (number is not null && await store.PersonnelNumberExistsAsync(number, employee.Id, context, cancellationToken))
            throw new PersonValidationException(["personnelNumber"]);
        var previous = PersonRules.Fields(employee);
        employee.FirstName = fields.FirstName!.Trim();
        employee.LastName = fields.LastName!.Trim();
        employee.Phone = fields.Phone;
        employee.PersonnelNumber = number;
        employee.Email = fields.Email?.Trim() ?? "";
        employee.StartDate = fields.StartDate;
        employee.EmploymentType = fields.EmploymentType!;
        employee.LanguagePref = fields.LanguagePref!;
        employee.AdrRequired = fields.AdrRequired;
        employee.VacationEntitlementDays = fields.VacationEntitlementDays;
        var updated = PersonRules.Fields(employee);
        var audit = new List<PersonAudit>();
        if (create) audit.Add(new("created", null, "onboarding"));
        foreach (var property in typeof(PersonFields).GetProperties())
        {
            var before = create ? null : JsonSerializer.Serialize(property.GetValue(previous));
            var after = JsonSerializer.Serialize(property.GetValue(updated));
            if (before != after) audit.Add(new(JsonNamingPolicy.CamelCase.ConvertName(property.Name), before, after));
        }
        if (create) audit.Add(new("roles", null, employee.Roles));
        await store.SaveAsync(employee, create, audit, actor, context, cancellationToken);
    }
}
