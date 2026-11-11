using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
public sealed class AbsenceQueries(IAbsenceStore store) : IQueryHandler<ListAbsencesQuery, AbsenceList>, IQueryHandler<GetAbsenceQuery, AbsenceDetail>, IQueryHandler<PreviewAbsenceQuery, AbsencePreview>, IQueryHandler<GetVacationBalanceQuery, VacationBalance>
{
    public async Task<AbsenceList> HandleAsync(ListAbsencesQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        if (query.From == default || query.To < query.From || query.To.DayNumber - query.From.DayNumber > 3660) throw new AbsenceValidationException("from", "to");
        var rows = await store.ListAsync(query.From, query.To, query.EmployeeId, null, cancellationToken);
        var details = new List<AbsenceDetail>();
        foreach (var absence in rows) details.Add(await Detail(absence, actor, cancellationToken));
        return new(details);
    }
    public async Task<AbsenceDetail> HandleAsync(GetAbsenceQuery query, AccessContext actor, CancellationToken cancellationToken) =>
        await Detail(await store.LoadAsync(query.Id, null, cancellationToken) ?? throw new AbsenceConflictException("not_found"), actor, cancellationToken);
    public async Task<VacationBalance> HandleAsync(GetVacationBalanceQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        if (query.Year < 1 || query.Year > 9999) throw new AbsenceValidationException("year");
        var facts = await store.FactsAsync(query.EmployeeId, null, cancellationToken) ?? throw new AbsenceConflictException("not_found");
        return AbsenceRules.Balance(facts, query.Year);
    }
    public async Task<AbsencePreview> HandleAsync(PreviewAbsenceQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        AbsenceRules.Validate(query.From, query.To, query.HalfDay, query.Type, null);
        var employeeId = query.EmployeeId ?? actor.EmployeeId;
        var facts = await store.FactsAsync(employeeId, null, cancellationToken) ?? throw new AbsenceConflictException("not_found");
        var absence = new Absence { EmployeeId = employeeId, From = query.From, To = query.To, HalfDay = query.HalfDay, Type = query.Type };
        decimal counted = 0;
        decimal before = 0;
        decimal after = 0;
        var overBalance = false;
        for (var year = query.From.Year; year <= query.To.Year; year++)
        {
            var days = AbsenceRules.Days(absence, year, facts.Holidays);
            var balance = AbsenceRules.Balance(facts, year).Remaining;
            var remaining = balance - (query.Type == AbsenceType.Vacation ? days : 0);
            counted += days;
            before += balance;
            after += remaining;
            overBalance |= remaining < 0;
        }
        var others = await store.OthersAsync(absence, null, cancellationToken);
        var visible = actor.Roles.Contains(RoleCode.Hr) || actor.Roles.Contains(RoleCode.Owner) || employeeId == actor.EmployeeId && actor.Roles.Contains(RoleCode.Driver);
        return new(counted, visible ? before : null, visible ? after : null, visible ? overBalance : null, others.Count);
    }
    private async Task<AbsenceDetail> Detail(Absence absence, AccessContext actor, CancellationToken cancellationToken)
    {
        var facts = await store.FactsAsync(absence.EmployeeId, null, cancellationToken) ?? throw new AbsenceConflictException("not_found");
        var before = new List<VacationBalance>();
        var after = new List<VacationBalance>();
        decimal days = 0;
        for (var year = absence.From.Year; year <= absence.To.Year; year++)
        {
            var balance = AbsenceRules.Balance(facts, year);
            var counted = AbsenceRules.Days(absence, year, facts.Holidays);
            days += counted;
            var deduction = absence.Type == AbsenceType.Vacation && absence.Status == AbsenceStatus.Requested ? counted : 0;
            before.Add(absence.Type == AbsenceType.Vacation && absence.Status == AbsenceStatus.Approved ? balance with { Used = balance.Used - counted, Remaining = balance.Remaining + counted } : balance);
            after.Add(balance with { Used = balance.Used + deduction, Remaining = balance.Remaining - deduction });
        }
        var others = await store.OthersAsync(absence, null, cancellationToken);
        return new(AbsenceRules.View(absence) with { WorkingDays = days }, facts.EmployeeName, days, before, after, others.Count, actor.Roles.Contains(RoleCode.Hr) || actor.Roles.Contains(RoleCode.Owner) ? others : [], "Mon-Fri without Company holidays (to confirm with payroll)");
    }
}
