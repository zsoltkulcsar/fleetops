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
        services.AddScoped<IComplianceRuleStore, EfComplianceRuleStore>();
        services.AddScoped<IQueryHandler<GetComplianceRulesQuery, ComplianceRulesView>, ComplianceRulesHandler>();
        services.AddScoped<ICommandHandler<EditComplianceRuleCommand, ComplianceRulesView>, ComplianceRulesHandler>();
        services.AddScoped<ICommandHandler<ConfirmComplianceRuleCommand, ComplianceRulesView>, ComplianceRulesHandler>();
        services.AddScoped<ICompanySettingsStore, EfCompanySettingsStore>();
        services.AddScoped<IReleaseSettings, EfCompanySettingsStore>();
        services.AddScoped<IQueryHandler<GetSettingsQuery, SettingsView>, SettingsHandler>();
        services.AddScoped<ICommandHandler<UpdateSettingsCommand, SettingsView>, SettingsHandler>();
        services.AddScoped<ICommandHandler<AddHolidayCommand, SettingsView>, SettingsHandler>();
        services.AddScoped<ICommandHandler<RemoveHolidayCommand, SettingsView>, SettingsHandler>();
    }
    public static void MapCompanySettings(this WebApplication app)
    {
        app.MapPost("/api/v1/alerts/{id:guid}/acknowledge-rule-warning", async (Guid id, RuleWarningAcknowledgement request, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<FleetOps.Modules.Alerts.AcknowledgeRuleWarningCommand, FleetOps.Modules.Alerts.ReleaseReviewResult>(new(id, request.Version, request.Note), cancellationToken))).Produces<FleetOps.Modules.Alerts.ReleaseReviewResult>();
        app.MapGet("/api/v1/settings/compliance-rules", async (QueryDispatcher queries, CancellationToken cancellationToken) =>
            Results.Ok(await queries.DispatchAsync<GetComplianceRulesQuery, ComplianceRulesView>(new(), cancellationToken))).Produces<ComplianceRulesView>();
        app.MapPut("/api/v1/settings/compliance-rules/{id}", async (string id, ComplianceRuleEditRequest request, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<EditComplianceRuleCommand, ComplianceRulesView>(new(id, request.Value?.GetRawText()), cancellationToken))).Produces<ComplianceRulesView>();
        app.MapPost("/api/v1/settings/compliance-rules/{id}/confirm", async (string id, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<ConfirmComplianceRuleCommand, ComplianceRulesView>(new(id), cancellationToken))).Produces<ComplianceRulesView>();
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
