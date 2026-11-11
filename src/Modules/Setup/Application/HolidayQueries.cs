using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
public sealed class HolidayQueries(ICompanySettingsStore store) : IQueryHandler<ListHolidaysQuery, HolidayList>
{
    public async Task<HolidayList> HandleAsync(ListHolidaysQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        if (query.From == default || query.To < query.From || query.To.DayNumber - query.From.DayNumber > 3660) throw new SettingsValidationException(["from", "to"]);
        var settings = await store.ReadAsync(null, cancellationToken);
        return new(settings.Holidays.Where(holiday => holiday.Date >= query.From && holiday.Date <= query.To).OrderBy(holiday => holiday.Date).ToArray());
    }
}
