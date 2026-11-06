using FleetOps.Identity;
using FleetOps.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed partial class FleetOpsDbContext
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<OfficeSession> OfficeSessions => Set<OfficeSession>();
    public DbSet<PrivacyAcknowledgement> PrivacyAcknowledgements => Set<PrivacyAcknowledgement>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
}
