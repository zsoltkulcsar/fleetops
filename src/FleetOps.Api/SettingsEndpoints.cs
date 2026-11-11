using FleetOps.Api.Pipeline;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;
using FleetOps.Migrations;
namespace FleetOps.Api;
public static class SettingsEndpoints
{
    public static void AddCompanySettings(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<ListHolidaysQuery, HolidayList>, HolidayQueries>();
        services.AddScoped<ICompanySettingsStore, EfCompanySettingsStore>();
        services.AddScoped<IReleaseSettings, EfCompanySettingsStore>();
        services.AddScoped<IQueryHandler<GetSettingsQuery, SettingsView>, SettingsHandler>();
        services.AddScoped<ICommandHandler<UpdateSettingsCommand, SettingsView>, SettingsHandler>();
        services.AddScoped<ICommandHandler<AddHolidayCommand, SettingsView>, SettingsHandler>();
        services.AddScoped<ICommandHandler<RemoveHolidayCommand, SettingsView>, SettingsHandler>();
    }
    public static void MapCompanySettings(this WebApplication app)
    {
        app.MapGet("/api/v1/holidays", async (DateOnly from, DateOnly to, QueryDispatcher queries, CancellationToken cancellationToken) =>
            Results.Ok(await queries.DispatchAsync<ListHolidaysQuery, HolidayList>(new(from, to), cancellationToken))).Produces<HolidayList>();
        app.MapGet("/api/v1/settings", async (QueryDispatcher queries, CancellationToken cancellationToken) =>
            Results.Ok(await queries.DispatchAsync<GetSettingsQuery, SettingsView>(new(), cancellationToken))).Produces<SettingsView>();
        app.MapPut("/api/v1/settings", async (UpdateSettingsRequest changes, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<UpdateSettingsCommand, SettingsView>(new(changes.ToChanges()), cancellationToken))).Produces<SettingsView>();
        app.MapPost("/api/v1/settings/holidays", async (AddHolidayCommand command, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<AddHolidayCommand, SettingsView>(command, cancellationToken))).Produces<SettingsView>();
        app.MapDelete("/api/v1/settings/holidays/{date}", async (DateOnly date, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<RemoveHolidayCommand, SettingsView>(new(date), cancellationToken))).Produces<SettingsView>();
    }
}
