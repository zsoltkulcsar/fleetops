using FleetOps.Modules.Setup;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed partial class FleetOpsDbContext
{
    public DbSet<FleetOps.Modules.Shifts.ShiftRuleGuidance> ShiftRuleGuidance => Set<FleetOps.Modules.Shifts.ShiftRuleGuidance>();
    public DbSet<ComplianceRule> ComplianceRules => Set<ComplianceRule>();
    public DbSet<RuleConfirmation> RuleConfirmations => Set<RuleConfirmation>();
    private static void ConfigureComplianceRules(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FleetOps.Modules.Shifts.ShiftRuleGuidance>(entity =>
        {
            entity.ToTable("shift_rule_guidance");
            entity.HasKey(state => state.ShiftId);
            entity.Property(state => state.Warnings).HasColumnType("jsonb");
            entity.HasOne<FleetOps.Modules.Shifts.Shift>().WithOne().HasForeignKey<FleetOps.Modules.Shifts.ShiftRuleGuidance>(state => state.ShiftId);
        });
        modelBuilder.Entity<ComplianceRule>(entity =>
        {
            entity.ToTable("compliance_rules");
            entity.HasKey(rule => rule.Id);
            entity.Property(rule => rule.Id).HasMaxLength(16);
            entity.Property(rule => rule.Name).HasMaxLength(200);
            entity.Property(rule => rule.GuideValue).HasMaxLength(1000);
            entity.Property(rule => rule.Value).HasColumnType("jsonb");
            entity.Property(rule => rule.ValueSchema).HasColumnType("jsonb");
            entity.HasData(
                new ComplianceRule { Id = "C-01", Name = "Max daily driving", GuideValue = "9 h; 10 h allowed max. 2\u00d7 per week", ValueSchema = "{\"minutes\":540,\"extendedMinutes\":600,\"extensionsPerWeek\":2}", Value = "{\"minutes\":540,\"extendedMinutes\":600,\"extensionsPerWeek\":2}" },
                new ComplianceRule { Id = "C-02", Name = "Max weekly driving", GuideValue = "56 h", ValueSchema = "{\"minutes\":3360}", Value = "{\"minutes\":3360}" },
                new ComplianceRule { Id = "C-03", Name = "Max driving in 2 consecutive weeks", GuideValue = "90 h", ValueSchema = "{\"minutes\":5400}", Value = "{\"minutes\":5400}" },
                new ComplianceRule { Id = "C-04", Name = "Break after driving", GuideValue = "\u226545 min after 4 h 30 min driving (split 15 + 30 allowed \u2014 verify)", ValueSchema = "{\"drivingMinutes\":270,\"breakMinutes\":45,\"splitFirstMinutes\":15,\"splitLastMinutes\":30}", Value = "{\"drivingMinutes\":270,\"breakMinutes\":45,\"splitFirstMinutes\":15,\"splitLastMinutes\":30}" },
                new ComplianceRule { Id = "C-05", Name = "Daily rest", GuideValue = "\u226511 h; may reduce to \u22659 h max. 3\u00d7 between weekly rests", ValueSchema = "{\"minutes\":660,\"reducedMinutes\":540,\"reductions\":3}", Value = "{\"minutes\":660,\"reducedMinutes\":540,\"reductions\":3}" },
                new ComplianceRule { Id = "C-06", Name = "Weekly rest", GuideValue = "\u226545 h regular; \u226524 h reduced (compensation rules apply)", ValueSchema = "{\"minutes\":2700,\"reducedMinutes\":1440}", Value = null },
                new ComplianceRule { Id = "C-07", Name = "Working-time breaks", GuideValue = "30 min if >6 h work; 45 min if >9 h", ValueSchema = "{\"workMinutes\":360,\"breakMinutes\":30,\"longWorkMinutes\":540,\"longBreakMinutes\":45}", Value = "{\"workMinutes\":360,\"breakMinutes\":30,\"longWorkMinutes\":540,\"longBreakMinutes\":45}" },
                new ComplianceRule { Id = "C-08", Name = "Weekly working time (mobile workers)", GuideValue = "max 60 h in a week, average \u226448 h; reference period NEEDS_CONFIRMATION (4 months standard)", ValueSchema = "{\"minutes\":3600,\"averageMinutes\":2880}", Value = null });
        });
        modelBuilder.Entity<RuleConfirmation>(entity =>
        {
            entity.ToTable("rule_confirmations");
            entity.HasKey(confirmation => confirmation.Id);
            entity.Property(confirmation => confirmation.Value).HasColumnType("jsonb");
            entity.HasOne<ComplianceRule>().WithMany().HasForeignKey(confirmation => confirmation.RuleId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
