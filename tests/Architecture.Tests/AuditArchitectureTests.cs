using FleetOps.Modules.Audit;
using FleetOps.Modules.Fleet;
using Xunit;

namespace FleetOps.Tests;

public sealed class AuditArchitectureTests
{
    [Fact]
    public void Audited_entities_declare_every_field_class()
    {
        var errors = AuditArchitectureRules.ValidateAssembly(typeof(BadAuditedEntity).Assembly);
        Assert.Contains(errors, x => x.Contains(nameof(BadAuditedEntity.MissingClass)));
    }

    [Fact]
    public async Task Every_vehicle_field_declares_an_audit_class()
    {
        var errors = AuditArchitectureRules.ValidateAssembly(typeof(Vehicle).Assembly);
        Assert.Empty(errors);
        await Task.CompletedTask;
    }
}

[AuditedEntity]
public sealed class BadAuditedEntity
{
    [AuditField(AuditFieldClasses.Plain)] public string Good { get; set; } = "";
    public string MissingClass { get; set; } = "";
}
