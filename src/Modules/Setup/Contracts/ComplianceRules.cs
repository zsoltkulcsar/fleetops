using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ComplianceRuleView(string Id, string Name, string GuideValue, string? Value, bool Confirmed)
{
    public string ValueSchema { get; init; } = "{}";
}
[VisibleTo(RoleCode.Owner)]
public sealed record ComplianceRulesView(IReadOnlyList<ComplianceRuleView> Rules, int Confirmed, int Total) : IReadModel;
[QueryPolicy("settings.manage")]
public sealed record GetComplianceRulesQuery : IQuery<ComplianceRulesView>;
[CommandPolicy("settings.manage")]
public sealed record EditComplianceRuleCommand(string RuleId, string? Value) : ICommand<ComplianceRulesView>;
[CommandPolicy("settings.manage")]
public sealed record ConfirmComplianceRuleCommand(string RuleId) : ICommand<ComplianceRulesView>;
public interface IComplianceRuleStore
{
    Task<IReadOnlyList<ComplianceRuleView>> ReadAsync(CommandContext? context, CancellationToken cancellationToken);
    Task UpdateAsync(string ruleId, string? value, bool confirm, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
