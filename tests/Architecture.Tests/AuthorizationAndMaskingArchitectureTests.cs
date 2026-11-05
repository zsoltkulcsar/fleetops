using FleetOps.Kernel.Authorization;
using FleetOps.Tests.Domain;
using System.Reflection;
using Xunit;

namespace FleetOps.Tests;

public sealed class AuthorizationAndMaskingArchitectureTests
{
    [Fact]
    public void Command_without_policy_is_reported() =>
        Assert.Contains(CommandArchitectureRules.ValidateAssembly(typeof(AuthorizationAndMaskingArchitectureTests).Assembly), x => x.Contains(nameof(CommandWithoutPolicy)));

    [Fact]
    public void Query_handler_without_context_is_reported() =>
        Assert.Contains(QueryArchitectureRules.ValidateAssembly(typeof(AuthorizationAndMaskingArchitectureTests).Assembly), x => x.Contains(nameof(QueryWithoutContextHandler)));

    [Fact]
    public void Query_returning_domain_is_reported() =>
        Assert.Contains(QueryArchitectureRules.ValidateAssembly(typeof(AuthorizationAndMaskingArchitectureTests).Assembly), x => x.Contains(nameof(DomainQuery)));

    [Fact]
    public void Query_nested_domain_member_is_reported_even_when_output_is_not_dispatcher_visible()
    {
        var errors = QueryArchitectureRules.ValidateAssembly(typeof(AuthorizationAndMaskingArchitectureTests).Assembly);
        Assert.Contains(errors, x => x.Contains(nameof(NestedDomainQuery)) && x.Contains(nameof(NestedDomainMember)));
    }

    [Fact]
    public void Query_rules_report_non_read_model_unknown_policy_and_missing_policy_separately()
    {
        var errors = QueryArchitectureRules.ValidateAssembly(typeof(AuthorizationAndMaskingArchitectureTests).Assembly);
        Assert.Contains(errors, x => x.Contains(nameof(NonReadModelQuery)) && x.Contains("IReadModel"));
        Assert.Contains(errors, x => x.Contains(nameof(UnknownPolicyQuery)) && x.Contains("unknown policy"));
        Assert.Contains(errors, x => x.Contains(nameof(NoPolicyQuery)) && x.Contains("no policy"));
    }

    [Fact]
    public void Direct_medical_member_is_reported() =>
        Assert.Contains(RoleMaskingInspector.ValidateDispatcherOutput(typeof(DirectMedicalOutput)), x => x.Contains("medical", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Nested_generic_medical_member_is_reported() =>
        Assert.Contains(RoleMaskingInspector.ValidateDispatcherOutput(typeof(NestedMedicalOutput)), x => x.Contains(nameof(MedicalLeaf)));

    [Fact]
    public void Class_level_medical_content_is_reported() =>
        Assert.Contains(RoleMaskingInspector.ValidateDispatcherOutput(typeof(ClassMedicalOutput)), x => x.Contains("medical", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Class_level_medical_type_visible_to_dispatcher_is_reported()
    {
        var errors = RoleMaskingInspector.ValidateDispatcherOutput(typeof(ClassMedicalVisibleToDispatcherOutput));
        Assert.Contains(errors, x => x.Contains("medical", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(errors, x => x.Contains("no VisibleTo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dispatcher_output_with_a_domain_type_is_reported()
    {
        var errors = RoleMaskingInspector.ValidateDispatcherOutput(typeof(DomainVisibleToDispatcherOutput));
        Assert.Contains(errors, x => x.Contains("contains Domain type"));
    }

    [Fact]
    public void Non_generic_collection_member_is_reported()
    {
        var errors = RoleMaskingInspector.ValidateDispatcherOutput(typeof(NonGenericCollectionOutput));
        Assert.Contains(errors, x => x.Contains("is a non-generic collection"));
    }

    [Fact]
    public void Unknown_action_key_is_reported()
    {
        var errors = CommandArchitectureRules.ValidateAssembly(typeof(AuthorizationAndMaskingArchitectureTests).Assembly);
        Assert.Contains(errors, x => x.Contains(nameof(UnknownCommandPolicy)) && x.Contains("unknown policy"));
    }

    [Fact]
    public void Interface_and_abstract_members_are_reported_as_uninspectable() 
    {
        var errors = RoleMaskingInspector.ValidateDispatcherOutput(typeof(InterfaceAndAbstractOutput));
        Assert.Contains(errors, x => x.Contains(nameof(InterfaceAndAbstractOutput.InterfaceValue)) && x.Contains("uninspectable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, x => x.Contains(nameof(InterfaceAndAbstractOutput.AbstractValue)) && x.Contains("uninspectable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generic_wrapper_members_are_inspected() =>
        Assert.Contains(RoleMaskingInspector.ValidateDispatcherOutput(typeof(GenericMedicalOutput)), x => x.Contains("GenericMedicalWrapper") && x.Contains("Secret"));

    [Fact]
    public void Public_fields_and_uninspectable_members_are_reported()
    {
        var errors = RoleMaskingInspector.ValidateDispatcherOutput(typeof(FieldAndUninspectableOutput));
        Assert.Contains(errors, x => x.Contains(nameof(FieldAndUninspectableOutput.MedicalField)) && x.Contains(nameof(MedicalLeaf)));
        Assert.Contains(errors, x => x.Contains(nameof(FieldAndUninspectableOutput.Unknown)) && x.Contains("uninspectable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Output_without_visible_to_is_reported() =>
        Assert.Contains(RoleMaskingInspector.ValidateDispatcherOutput(typeof(UnmarkedOutput)), x => x.Contains("VisibleTo"));

    [Fact]
    public void Assembly_sweep_derives_module_folders_and_loads_required_production_assemblies()
    {
        var moduleRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Modules"));
        var names = Directory.EnumerateDirectories(moduleRoot).Select(Path.GetFileName).Where(x => x is not null).Cast<string>()
            .Concat(["FleetOps.Api", "FleetOps.Migrations", "Kernel"]).ToArray();
        var loaded = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll").Select(Assembly.LoadFrom).ToDictionary(x => x.GetName().Name!, StringComparer.Ordinal);
        Assert.All(names, name => Assert.True(loaded.ContainsKey(name), $"Production assembly {name} was not found."));
        var assemblies = names.Select(name => loaded[name]).ToArray();
        var errors = assemblies.SelectMany(assembly =>
            RoleMaskingInspector.ValidateAssembly(assembly)
                .Concat(CommandArchitectureRules.ValidateAssembly(assembly))
                .Concat(QueryArchitectureRules.ValidateAssembly(assembly)))
            .ToArray();
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateAssembly_reports_all_missing_visible_to_and_own_subject_rules()
    {
        var errors = RoleMaskingInspector.ValidateAssembly(typeof(MissingVisibleReadModel).Assembly)
            .Concat(CommandArchitectureRules.ValidateAssembly(typeof(MissingVisibleReadModel).Assembly)).ToArray();
        Assert.Contains(errors, x => x.Contains(nameof(MissingVisibleReadModel)));
        Assert.Contains(errors, x => x.Contains(nameof(MissingOutboundPayload)));
        Assert.Contains(errors, x => x.Contains("MissingCommandResult"));
        Assert.Contains(errors, x => x.Contains(nameof(MissingOwnSubjectCommand)));
    }
}

public sealed class CommandWithoutPolicy : ICommand { }
public sealed class DomainQuery : IQuery<FixtureDomain> { }
public sealed class QueryWithoutContextHandler
{
    public Task<FixtureReadModel> HandleAsync(DomainQuery query, CancellationToken cancellationToken) => Task.FromResult(new FixtureReadModel());
}
public sealed class FixtureReadModel : IReadModel { }

[VisibleTo(RoleCode.Dispatcher)]
public sealed class DirectMedicalOutput : IReadModel
{
    [MedicalContent] public string Medical { get; init; } = "secret";
}

[VisibleTo(RoleCode.Dispatcher)]
public sealed class NestedMedicalOutput : IReadModel
{
    public List<MedicalLeaf> Values { get; init; } = [];
}

[VisibleTo(RoleCode.Dispatcher)]
public sealed class GenericMedicalOutput : IReadModel
{
    public GenericMedicalWrapper<MedicalLeaf> Items { get; init; } = new();
}

public sealed class GenericMedicalWrapper<T>
{
    [MedicalContent] public string Secret { get; init; } = "secret";
    public T? Leaf { get; init; }
}

[VisibleTo(RoleCode.Dispatcher)]
public sealed class FieldAndUninspectableOutput : IReadModel
{
    [MedicalContent] public MedicalLeaf MedicalField = new();
    public object Unknown { get; init; } = new();
}

public sealed class MedicalLeaf
{
    [MedicalContent] public string Value { get; init; } = "secret";
}

public sealed class UnmarkedOutput : IReadModel { public string Value { get; init; } = "ok"; }
[MedicalContent]
public sealed class ClassMedicalOutput : IReadModel { }
[MedicalContent, VisibleTo(RoleCode.Dispatcher)]
public sealed class ClassMedicalVisibleToDispatcherOutput : IReadModel { }
[VisibleTo(RoleCode.Dispatcher)]
public sealed class DomainVisibleToDispatcherOutput : IReadModel { public FixtureDomain Value { get; init; } = new(); }
[VisibleTo(RoleCode.Dispatcher)]
public sealed class NonGenericCollectionOutput : IReadModel { public System.Collections.ArrayList Values { get; init; } = []; }
public interface IFixtureValue { }
public abstract class AbstractFixtureValue { }
[VisibleTo(RoleCode.Dispatcher)]
public sealed class InterfaceAndAbstractOutput : IReadModel
{
    public IFixtureValue InterfaceValue { get; init; } = null!;
    public AbstractFixtureValue AbstractValue { get; init; } = null!;
}
public sealed class MissingVisibleReadModel : IReadModel { }
public sealed class MissingOutboundPayload : IOutboundPayload { }
public sealed class MissingCommandResult : ICommand<MissingVisibleReadModel> { }
[CommandPolicy("employees.view")]
public sealed class MissingOwnSubjectCommand : ICommand<int> { }
[QueryPolicy("employees.view")]
public sealed class NestedDomainQuery : IQuery<NestedDomainOutput> { }
[VisibleTo(RoleCode.Hr)]
public sealed class NestedDomainOutput : IReadModel { public NestedDomainMember Value { get; init; } = new(); }
public sealed class NonReadModelQuery : IQuery<string> { }
[QueryPolicy("missing.permission")]
public sealed class UnknownPolicyQuery : IQuery<FixtureReadModel> { }
public sealed class NoPolicyQuery : IQuery<FixtureReadModel> { }
[CommandPolicy("no.such.key")]
public sealed class UnknownCommandPolicy : ICommand { }
