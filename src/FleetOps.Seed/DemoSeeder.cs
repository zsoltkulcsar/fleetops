using System.Text.Json;
using FleetOps.Identity;
using FleetOps.Migrations;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Setup;
using FleetOps.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Seed;

public sealed class DemoSeeder
{
    private static readonly Guid DepotId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateTimeOffset SeedTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly Func<int, Exception?>? failureFactory;

    public DemoSeeder(Func<int, Exception?>? failureFactory = null)
    {
        this.failureFactory = failureFactory;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnvironment();
        var password = Environment.GetEnvironmentVariable("FLEETOPS_DEMO_PASSWORD");
        if (string.IsNullOrEmpty(password) || password.Length < 12)
        {
            throw new InvalidOperationException("FLEETOPS_DEMO_PASSWORD must be at least 12 characters.");
        }

        var connectionString = DesignTimeFactory.ResolveConnectionString();
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        options.UseNpgsql(connectionString);
        await using var db = new FleetOpsDbContext(options.Options);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ResetAsync(db, cancellationToken);
        ThrowIfRequested(1);
        AddEmployees(db);
        AddVehicles(db);
        await db.SaveChangesAsync(cancellationToken);
        ThrowIfRequested(2);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void EnsureEnvironment()
    {
        var environment = Environment.GetEnvironmentVariable("FLEETOPS_ENVIRONMENT");
        if (environment is not ("Local" or "Demo" or "Rehearsal"))
        {
            throw new InvalidOperationException("FLEETOPS_ENVIRONMENT must be Local, Demo or Rehearsal.");
        }
    }

    private async Task ResetAsync(FleetOpsDbContext db, CancellationToken cancellationToken)
    {
        var tables = new[] { "audit_entries", "document_files", "documents", "pending_files", "vehicle_plate_history", "vehicles", "depots", "user_tokens", "user_logins", "user_claims", "user_roles", "role_claims", "sessions", "privacy_acknowledgements", "password_reset_tokens", "users", "roles", "employees" };
        foreach (var table in tables)
        {
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM fleetops.\"{table}\"", cancellationToken);
        }

        db.Depots.Add(new Depot { Id = DepotId, Name = "Default depot", Address = "Default address" });
    }

    private void AddEmployees(FleetOpsDbContext db)
    {
        var password = Environment.GetEnvironmentVariable("FLEETOPS_DEMO_PASSWORD")!;
        var hasher = new PasswordHasher<OfficeUser>();
        var accounts = new[]
        {
            ("Konrad", "Fischer", "owner", "owner"), ("Maren", "Vogt", "hr", "hr"),
            ("Jens", "Krause", "dispatcher1", "dispatcher"), ("Lina", "Brandt", "dispatcher2", "dispatcher"),
        };
        for (var index = 0; index < accounts.Length; index++)
        {
            var account = accounts[index];
            var employeeId = FixedGuid(1 + index);
            var email = $"{account.Item3}@demo.fleetops.example";
            db.Employees.Add(new Employee { Id = employeeId, FirstName = account.Item1, LastName = account.Item2, Email = email, Roles = JsonSerializer.Serialize(new[] { account.Item4 }), StartDate = StartDate });
            var user = new OfficeUser { Id = FixedGuid(101 + index), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true, EmployeeId = employeeId, LockoutEnabled = true, SecurityStamp = FixedGuid(201 + index).ToString(), ConcurrencyStamp = FixedGuid(301 + index).ToString() };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
        }

        var roles = new[] { ("Owner", 401), ("Hr", 402), ("Dispatcher", 403) };
        foreach (var role in roles)
        {
            db.Roles.Add(new IdentityRole<Guid> { Id = FixedGuid(role.Item2), Name = role.Item1, NormalizedName = role.Item1.ToUpperInvariant(), ConcurrencyStamp = FixedGuid(role.Item2 + 100).ToString() });
        }

        db.UserRoles.AddRange(new IdentityUserRole<Guid> { UserId = FixedGuid(101), RoleId = FixedGuid(401) }, new IdentityUserRole<Guid> { UserId = FixedGuid(102), RoleId = FixedGuid(402) }, new IdentityUserRole<Guid> { UserId = FixedGuid(103), RoleId = FixedGuid(403) }, new IdentityUserRole<Guid> { UserId = FixedGuid(104), RoleId = FixedGuid(403) });
        for (var index = 0; index < 24; index++)
        {
            var firstName = new[] { "Bastian", "Claus", "Dirk", "Erik", "Falk", "Gerd", "Hannes", "Ivo", "Jochen", "Kai", "Lars", "Mats" }[index % 12];
            var lastName = new[] { "Albrecht", "Becker", "Dreher", "Eckert", "Franke", "Günther", "Hartmann", "Jäger" }[index % 8];
            db.Employees.Add(new Employee { Id = FixedGuid(10 + index), FirstName = firstName, LastName = lastName, Email = $"driver{index + 1}@demo.fleetops.example", Roles = "[\"driver\"]", StartDate = StartDate });
        }
    }

    private static void AddVehicles(FleetOpsDbContext db)
    {
        for (var index = 0; index < 35; index++)
        {
            var truck = index < 20;
            var ownership = (VehicleOwnership)(index % 3);
            db.Vehicles.Add(new Vehicle { Id = FixedGuid(1000 + index), Kind = truck ? VehicleKind.Truck : VehicleKind.Trailer, Plate = $"B-D {index + 1:0000}", Vin = $"WVWZZZ1JZXW{index + 1:000000}", Make = truck ? new[] { "MAN", "Mercedes-Benz", "Scania", "DAF" }[index % 4] : new[] { "Schmitz", "Kögel", "Krone" }[index % 3], Model = truck ? "TGX" : "S.KO", Year = 2020 + index % 6, GrossWeightKg = truck ? 18000 : 39000, RequiredLicenceClass = truck ? "C" : null, Ownership = ownership, LeaseEndDate = ownership == VehicleOwnership.Owned ? null : new DateOnly(2028, 12, 31), TrailerType = truck ? null : (TrailerType)(index % 5), OdometerKm = truck ? 10000 + index * 1000 : null, DepotId = DepotId, ServiceIntervalKm = truck ? 50000 : null, ServiceIntervalMonths = truck ? 12 : null, Status = VehicleStatus.Active, CreatedAt = SeedTime, UpdatedAt = SeedTime });
        }
    }

    private void ThrowIfRequested(int step)
    {
        var exception = failureFactory?.Invoke(step);
        if (exception is not null)
        {
            throw exception;
        }
    }

    private static Guid FixedGuid(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");
}
