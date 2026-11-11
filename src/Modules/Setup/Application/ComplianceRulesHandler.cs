using System.Text.Json;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
public sealed class ComplianceRulesHandler(IComplianceRuleStore store) :
    IQueryHandler<GetComplianceRulesQuery, ComplianceRulesView>,
    ICommandHandler<EditComplianceRuleCommand, ComplianceRulesView>,
    ICommandHandler<ConfirmComplianceRuleCommand, ComplianceRulesView>
{
    public async Task<ComplianceRulesView> HandleAsync(GetComplianceRulesQuery query, AccessContext actor, CancellationToken cancellationToken) =>
        View(await store.ReadAsync(null, cancellationToken));
    public async Task<ComplianceRulesView> HandleAsync(EditComplianceRuleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        ValidateId(command.RuleId);
        if (command.Value is not null)
        {
            if (command.Value.Length > 4000) throw new SettingsValidationException(["value"]);
            try
            {
                using var value = JsonDocument.Parse(command.Value);
                if (value.RootElement.ValueKind != JsonValueKind.Object || !value.RootElement.EnumerateObject().Any() ||
                    value.RootElement.EnumerateObject().Any(property => property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var number) || number <= 0))
                    throw new SettingsValidationException(["value"]);
                var current = (await store.ReadAsync(context, cancellationToken)).SingleOrDefault(rule => rule.Id == command.RuleId);
                if (current is null) throw new SettingsValidationException(["ruleId"]);
                using var schema = JsonDocument.Parse(current.ValueSchema);
                if (!schema.RootElement.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(value.RootElement.EnumerateObject().Select(property => property.Name).Order()))
                    throw new SettingsValidationException(["value"]);
            }
            catch (JsonException) { throw new SettingsValidationException(["value"]); }
        }
        await store.UpdateAsync(command.RuleId, command.Value, false, actor, context, cancellationToken);
        return View(await store.ReadAsync(context, cancellationToken));
    }
    public async Task<ComplianceRulesView> HandleAsync(ConfirmComplianceRuleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        ValidateId(command.RuleId);
        await store.UpdateAsync(command.RuleId, null, true, actor, context, cancellationToken);
        return View(await store.ReadAsync(context, cancellationToken));
    }
    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 16) throw new SettingsValidationException(["ruleId"]);
    }
    private static ComplianceRulesView View(IReadOnlyList<ComplianceRuleView> rules) => new(rules, rules.Count(rule => rule.Confirmed), rules.Count);
}
