using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
[QueryPolicy("holidays.view")]
public sealed record ListHolidaysQuery(DateOnly From, DateOnly To) : IQuery<HolidayList>;
