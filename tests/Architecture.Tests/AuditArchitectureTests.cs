using FleetOps.Modules.Audit;
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
}

[AuditedEntity]
public sealed class BadAuditedEntity
{
    [AuditField(AuditFieldClasses.Plain)] public string Good { get; set; } = "";
    public string MissingClass { get; set; } = "";
}
