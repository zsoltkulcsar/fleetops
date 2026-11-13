using System.Text.Json;
using FleetOps.Identity;
using FleetOps.Migrations;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Absences;
using FleetOps.Modules.Maintenance;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Setup;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MaintenanceJob = FleetOps.Modules.Maintenance.Maintenance;

namespace FleetOps.Seed;

public sealed class DemoSeeder
{
    private static readonly Guid DepotId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateTimeOffset SeedTime = DateTimeOffset.UtcNow;
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
        AddDrivers(db);
        AddVehicles(db);
        AddDocuments(db);
        AddOperations(db);
        AddHolidays(db);
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
        // Every table in the schema except migration history and the reference data migrations insert,
        // so tables added by later stories are cleared too. One TRUNCATE handles the foreign keys between them.
        var keep = new HashSet<string>(StringComparer.Ordinal) { "__EFMigrationsHistory", "compliance_rules", "migration_marker" };
        var tables = await db.Database.SqlQueryRaw<string>("SELECT tablename AS \"Value\" FROM pg_tables WHERE schemaname = 'fleetops'").ToListAsync(cancellationToken);
        var targets = string.Join(", ", tables.Where(table => !keep.Contains(table)).Order(StringComparer.Ordinal).Select(table => $"fleetops.\"{table}\""));
        await db.Database.ExecuteSqlRawAsync("TRUNCATE " + targets, cancellationToken);

        db.Depots.Add(new Depot { Id = DepotId, Name = "Default depot", Address = "Default address" });
    }

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly DateOnly Today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(SeedTime, Berlin).DateTime);
    private static readonly DateOnly Monday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));

    private static DateTimeOffset At(DateOnly date, int hour, int minute)
    {
        var local = date.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Berlin.GetUtcOffset(local)).ToUniversalTime();
    }

    // Eight drivers, one per main use case (ids 10-17, driver1..driver8@).
    private static readonly Guid Bastian = FixedGuid(10); // ready, on shift now, week approved
    private static readonly Guid Claus = FixedGuid(11);   // week submitted and awaiting approval, Code 95 expiring
    private static readonly Guid Dirk = FixedGuid(12);    // blocked: medical expired; planned days without hours
    private static readonly Guid Erik = FixedGuid(13);    // one rejected day, truck with overdue service
    private static readonly Guid Falk = FixedGuid(14);    // on approved vacation now
    private static readonly Guid Gerd = FixedGuid(15);    // driver card missing, leave request waiting
    private static readonly Guid Hannes = FixedGuid(16);  // onboarding, starts next week, not enrolled
    private static readonly Guid Ivo = FixedGuid(17);     // sick now, part time, English, ADR, planned next week

    private static void AddDrivers(FleetOpsDbContext db)
    {
        (Guid Id, string First, string Last, EmployeeStatus Status, string Employment, string Language, bool Adr, DateOnly Start)[] drivers =
        [
            (Bastian, "Bastian", "Albrecht", EmployeeStatus.Active, "full_time", "de", false, StartDate),
            (Claus, "Claus", "Becker", EmployeeStatus.Active, "full_time", "de", false, StartDate),
            (Dirk, "Dirk", "Dreher", EmployeeStatus.Active, "full_time", "de", false, StartDate),
            (Erik, "Erik", "Eckert", EmployeeStatus.Active, "full_time", "de", false, StartDate),
            (Falk, "Falk", "Franke", EmployeeStatus.Active, "full_time", "de", false, StartDate),
            (Gerd, "Gerd", "Günther", EmployeeStatus.Active, "full_time", "de", false, StartDate),
            (Hannes, "Hannes", "Hartmann", EmployeeStatus.Onboarding, "full_time", "de", false, Today.AddDays(7)),
            (Ivo, "Ivo", "Jäger", EmployeeStatus.Active, "part_time", "en", true, StartDate),
        ];
        for (var index = 0; index < drivers.Length; index++)
        {
            var driver = drivers[index];
            db.Employees.Add(new Employee
            {
                Id = driver.Id, FirstName = driver.First, LastName = driver.Last, Email = $"driver{index + 1}@demo.fleetops.example",
                PersonnelNumber = $"D-{index + 1:0000}", Phone = $"+49 170 555{index + 1:0000}", Roles = "[\"driver\"]", Status = driver.Status,
                DepotId = DepotId, EmploymentType = driver.Employment, LanguagePref = driver.Language, AdrRequired = driver.Adr, StartDate = driver.Start,
                VacationEntitlementDays = driver.Employment == "part_time" ? 15 : 30, VacationOpeningBalance = index % 3, VacationOpeningBalanceYear = Today.Year,
            });
        }
    }

    private static void AddVehicles(FleetOpsDbContext db)
    {
        (string Make, string Model)[] trucks = [("MAN", "TGX"), ("Mercedes-Benz", "Actros"), ("Scania", "R 450"), ("DAF", "XF"), ("MAN", "TGS"), ("Mercedes-Benz", "Actros L"), ("Scania", "S 500"), ("DAF", "XG")];
        (string Make, string Model)[] trailers = [("Schmitz", "S.KO Cool"), ("Krone", "Profi Liner"), ("Kögel", "Cargo"), ("Schmitz", "S.CS")];
        TrailerType[] trailerTypes = [TrailerType.Reefer, TrailerType.Curtainsider, TrailerType.Box, TrailerType.Curtainsider];
        for (var index = 0; index < trucks.Length + trailers.Length; index++)
        {
            var truck = index < trucks.Length;
            var ownership = (VehicleOwnership)(index % 3);
            db.Vehicles.Add(new Vehicle
            {
                Id = FixedGuid(1000 + index), Kind = truck ? VehicleKind.Truck : VehicleKind.Trailer, Plate = $"B-D {index + 1:0000}", Vin = $"WVWZZZ1JZXW{index + 1:000000}",
                Make = truck ? trucks[index].Make : trailers[index - trucks.Length].Make, Model = truck ? trucks[index].Model : trailers[index - trucks.Length].Model,
                Year = 2019 + index % 7, GrossWeightKg = truck ? 18000 : 39000, RequiredLicenceClass = truck ? "C" : null, Ownership = ownership,
                LeaseEndDate = ownership == VehicleOwnership.Owned ? null : new DateOnly(2028, 12, 31), TrailerType = truck ? null : trailerTypes[index - trucks.Length],
                OdometerKm = truck ? 120000 + index * 23000 : null, OdometerUpdatedAt = truck ? SeedTime : null, DepotId = DepotId,
                ServiceIntervalKm = truck ? 50000 : null, ServiceIntervalMonths = truck ? 12 : null, Status = VehicleStatus.Active, CreatedAt = SeedTime, UpdatedAt = SeedTime,
            });
        }
    }

    private static void AddDocuments(FleetOpsDbContext db)
    {
        foreach (var driver in new[] { Bastian, Claus, Dirk, Erik, Falk, Gerd, Hannes, Ivo })
        {
            DocumentType[] types = [DocumentType.LicenceC_CE, DocumentType.DriverCard, DocumentType.Code95, DocumentType.Medical];
            foreach (var type in types)
            {
                if (driver == Hannes && type != DocumentType.LicenceC_CE) continue; // onboarding: papers still coming
                if (driver == Gerd && type == DocumentType.DriverCard) continue; // missing
                var expires = (driver, type) switch
                {
                    _ when driver == Dirk && type == DocumentType.Medical => Today.AddDays(-10), // expired: blocked
                    _ when driver == Claus && type == DocumentType.Code95 => Today.AddDays(25), // expiring soon
                    _ when driver == Ivo && type == DocumentType.Medical => Today.AddDays(60),
                    _ => Today.AddDays(400 + 90 * Array.IndexOf(types, type)),
                };
                var document = Attested(driver, DocumentOwnerType.Employee, type, expires);
                if (type == DocumentType.LicenceC_CE) document.Categories = ["C", "CE"];
                db.Documents.Add(document);
            }
        }
        foreach (var vehicle in db.Vehicles.Local.ToList())
        {
            foreach (var required in VehicleRules.Documents(vehicle.Kind, vehicle.GrossWeightKg).Where(x => DocumentsCatalog.Get(MapDocumentType(x.Type)).Blocking))
            {
                var type = MapDocumentType(required.Type);
                if (vehicle.Id == FixedGuid(1000) && type == DocumentType.HuReport) db.Documents.Add(Attested(vehicle.Id, DocumentOwnerType.Vehicle, type, Today.AddDays(-10)));
                else if (vehicle.Id == FixedGuid(1001) && type == DocumentType.Insurance) db.Documents.Add(Attested(vehicle.Id, DocumentOwnerType.Vehicle, type, Today.AddDays(10)));
                else if (vehicle.Id == FixedGuid(1002) && type == DocumentType.TachoCalibration) continue;
                else db.Documents.Add(Attested(vehicle.Id, DocumentOwnerType.Vehicle, type, Today.AddDays(365)));
            }
        }
    }

    private static void AddOperations(FleetOpsDbContext db)
    {
        var hr = FixedGuid(2);

        // Plan: who drives which truck. Active cycles cover today; Ivo's starts next week.
        (int Id, Guid Driver, int Truck, DateOnly From, DateOnly To, CycleStatus Status)[] cycles =
        [
            (8000, Bastian, 1003, Monday.AddDays(-7), Monday.AddDays(11), CycleStatus.Active),
            (8001, Claus, 1001, Monday.AddDays(-7), Monday.AddDays(11), CycleStatus.Active),
            (8002, Dirk, 1006, Monday, Monday.AddDays(13), CycleStatus.Active),
            (8003, Erik, 1005, Monday.AddDays(-7), Monday.AddDays(11), CycleStatus.Active),
            (8004, Ivo, 1007, Monday.AddDays(7), Monday.AddDays(20), CycleStatus.Planned),
        ];
        foreach (var cycle in cycles)
            db.Cycles.Add(new Cycle { Id = FixedGuid(cycle.Id), EmployeeId = cycle.Driver, VehicleId = FixedGuid(cycle.Truck), From = cycle.From, To = cycle.To, Status = cycle.Status, Source = CycleSource.Manual });

        // Hours: last week approved for everyone on a cycle; this week depends on the driver. Dirk has none (missing days).
        var shiftId = 5000;
        var activityId = 6000;
        (Guid Driver, int Truck, int Trailer)[] working = [(Bastian, 1003, 1008), (Claus, 1001, 1009), (Erik, 1005, 1010)];
        foreach (var (driver, truck, trailer) in working)
        {
            var odometer = 120000 + (truck - 1000) * 23000 - 9000;
            for (var date = Monday.AddDays(-7); date < Today; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var thisWeek = date >= Monday;
                var status = !thisWeek || driver == Bastian ? ShiftStatus.Approved
                    : driver == Erik && date == Monday.AddDays(1) ? ShiftStatus.Rejected
                    : ShiftStatus.Submitted;
                var start = At(date, 6, 0);
                var end = At(date, 15, 30);
                var shift = new Shift
                {
                    Id = FixedGuid(shiftId++), EmployeeId = driver, Date = date, StartAt = start, EndAt = end, TruckId = FixedGuid(truck), TrailerId = FixedGuid(trailer),
                    OdometerStartKm = odometer, OdometerEndKm = odometer + 410, DepartureCheckOk = true, Status = status,
                    SubmittedAt = end.AddMinutes(10), DeviceTime = end.AddMinutes(10), ServerReceivedAt = end.AddMinutes(10),
                    ApprovedBy = status == ShiftStatus.Approved ? hr : null, ApprovedAt = status == ShiftStatus.Approved ? end.AddDays(1) : null,
                    RejectReason = status == ShiftStatus.Rejected ? "The break after 4.5 hours of driving is missing. Please add it and submit again." : null,
                };
                odometer += 410;
                (ActivityType Type, int FromH, int FromM, int ToH, int ToM)[] day = status == ShiftStatus.Rejected
                    ? [(ActivityType.OtherWork, 6, 0, 6, 15), (ActivityType.Driving, 6, 15, 12, 0), (ActivityType.OtherWork, 12, 0, 15, 30)]
                    : [(ActivityType.OtherWork, 6, 0, 6, 15), (ActivityType.Driving, 6, 15, 10, 45), (ActivityType.Break, 10, 45, 11, 30), (ActivityType.Driving, 11, 30, 14, 0), (ActivityType.OtherWork, 14, 0, 15, 30)];
                foreach (var part in day)
                    shift.Activities.Add(new ShiftActivity { Id = FixedGuid(activityId++), ShiftId = shift.Id, Type = part.Type, StartAt = At(date, part.FromH, part.FromM), EndAt = At(date, part.ToH, part.ToM), RecordedAt = At(date, part.ToH, part.ToM) });
                db.Shifts.Add(shift);
            }
        }

        // Bastian is on the road right now.
        var started = At(Today, 6, 0) < SeedTime ? At(Today, 6, 0) : SeedTime.AddHours(-1);
        var running = new Shift
        {
            Id = FixedGuid(shiftId), EmployeeId = Bastian, Date = Today, StartAt = started, TruckId = FixedGuid(1003), TrailerId = FixedGuid(1008),
            OdometerStartKm = 120000 + 3 * 23000, DepartureCheckOk = true, Status = ShiftStatus.Running, DeviceTime = started, ServerReceivedAt = started,
        };
        running.Activities.Add(new ShiftActivity { Id = FixedGuid(activityId++), ShiftId = running.Id, Type = ActivityType.OtherWork, StartAt = started, EndAt = started.AddMinutes(15), RecordedAt = started.AddMinutes(15) });
        running.Activities.Add(new ShiftActivity { Id = FixedGuid(activityId), ShiftId = running.Id, Type = ActivityType.Driving, StartAt = started.AddMinutes(15), RecordedAt = started.AddMinutes(15) });
        db.Shifts.Add(running);

        // Absences: vacation now, a waiting request, sick now, and a planned vacation for the capacity strip.
        (int Id, Guid Driver, AbsenceType Type, DateOnly From, DateOnly To, AbsenceStatus Status, string? Note)[] absences =
        [
            (7000, Falk, AbsenceType.Vacation, Monday.AddDays(-3), Monday.AddDays(11), AbsenceStatus.Approved, "Family holiday"),
            (7001, Gerd, AbsenceType.Vacation, Monday.AddDays(14), Monday.AddDays(18), AbsenceStatus.Requested, "Moving house"),
            (7002, Ivo, AbsenceType.Sick, Today, Today.AddDays(2), AbsenceStatus.Approved, null),
            (7003, Claus, AbsenceType.Vacation, Monday.AddDays(21), Monday.AddDays(25), AbsenceStatus.Approved, null),
        ];
        foreach (var absence in absences)
            db.Absences.Add(new Absence { Id = FixedGuid(absence.Id), EmployeeId = absence.Driver, Type = absence.Type, From = absence.From, To = absence.To, Status = absence.Status, Note = absence.Note, DecidedBy = absence.Status == AbsenceStatus.Approved ? hr : null });

        // Defects and the workshop.
        var repair = FixedGuid(9500);
        (int Id, int Vehicle, Guid By, int DaysAgo, DefectCategory Category, DefectSeverity Severity, DefectStatus Status, string Text, Guid? Repair)[] defects =
        [
            (9000, 1003, Bastian, 1, DefectCategory.Lights, DefectSeverity.Minor, DefectStatus.Open, "Left rear marker light is out.", null),
            (9001, 1004, Erik, 2, DefectCategory.Brakes, DefectSeverity.Critical, DefectStatus.InRepair, "Brake warning light on, pedal feels soft.", repair),
            (9002, 1009, Claus, 1, DefectCategory.Tyres, DefectSeverity.Major, DefectStatus.Open, "Tread low on axle 2, right side.", null),
        ];
        db.Maintenance.Add(new MaintenanceJob { Id = repair, Type = "repair", VehicleId = FixedGuid(1004), Workshop = "Werkstatt Nord (demo)", ScheduledFor = Today.AddDays(-1), DefectReportIds = [FixedGuid(9001)], Status = MaintenanceStatus.InProgress, DueDate = Today.AddDays(2) });
        foreach (var defect in defects)
        {
            var reported = At(Today.AddDays(-defect.DaysAgo), 7, 10);
            db.DefectReports.Add(new DefectReport { Id = FixedGuid(defect.Id), VehicleId = FixedGuid(defect.Vehicle), ReportedBy = defect.By, ReportedAt = reported, Origin = DefectOrigin.App, Category = defect.Category, Severity = defect.Severity, Description = defect.Text, Status = defect.Status, DeviceTime = reported, ServerReceivedAt = reported, RepairId = defect.Repair });
        }
        db.Maintenance.AddRange(
            new MaintenanceJob { Id = FixedGuid(9501), Type = "service", VehicleId = FixedGuid(1005), Workshop = "MAN Service Kassel (demo)", ScheduledFor = Today.AddDays(4), DueDate = Today.AddDays(-5), Status = MaintenanceStatus.Scheduled },
            new MaintenanceJob { Id = FixedGuid(9502), Type = "tacho_check", VehicleId = FixedGuid(1002), Workshop = "Tacho Service Süd (demo)", ScheduledFor = Today.AddDays(3), DueDate = Today.AddDays(3), Status = MaintenanceStatus.Scheduled },
            new MaintenanceJob { Id = FixedGuid(9503), Type = "inspection_hu", VehicleId = FixedGuid(1000), Workshop = "Prüfstelle Mitte (demo)", ScheduledFor = Today.AddDays(2), DueDate = Today.AddDays(2), Status = MaintenanceStatus.Scheduled },
            new MaintenanceJob { Id = FixedGuid(9504), Type = "service", VehicleId = FixedGuid(1003), Workshop = "MAN Service Kassel (demo)", ScheduledFor = Today.AddDays(-60), Status = MaintenanceStatus.Done, DoneAt = Today.AddDays(-60), OdometerAtDoneKm = 180000, CostEur = 840m });
    }

    private static void AddHolidays(FleetOpsDbContext db)
    {
        (string Date, string Name)[] holidays =
        [
            ("2026-01-01", "New Year's Day"), ("2026-04-03", "Good Friday"), ("2026-04-06", "Easter Monday"),
            ("2026-05-01", "Labour Day"), ("2026-05-14", "Ascension Day"), ("2026-05-25", "Whit Monday"),
            ("2026-10-03", "German Unity Day"), ("2026-12-25", "Christmas Day"), ("2026-12-26", "Second Christmas Day"),
            ("2027-01-01", "New Year's Day"), ("2027-03-26", "Good Friday"), ("2027-03-29", "Easter Monday"),
            ("2027-05-01", "Labour Day"), ("2027-05-06", "Ascension Day"), ("2027-05-17", "Whit Monday"),
            ("2027-10-03", "German Unity Day"), ("2027-12-25", "Christmas Day"), ("2027-12-26", "Second Christmas Day")
        ];
        foreach (var holiday in holidays)
            db.CompanyHolidays.Add(new CompanyHoliday { Date = DateOnly.ParseExact(holiday.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Name = holiday.Name + " - Check against your Bundesland (unverified)" });
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
    }

    private static Document Attested(Guid ownerId, DocumentOwnerType ownerType, DocumentType type, DateOnly expiresAt) => new() { OwnerId = ownerId, OwnerType = ownerType, Type = type, Number = $"DEMO-{ownerId.ToString("N")[..8]}-{type}", Status = DocumentStatus.Verified, ExpiresAt = expiresAt, IssuedAt = Today.AddYears(-1), ImportAttested = true, CreatedBy = FixedGuid(1), CreatedAt = SeedTime, VerifiedBy = FixedGuid(1), VerifiedAt = SeedTime };
    private static DocumentType MapDocumentType(string type) => type switch { "registration" => DocumentType.Registration, "HU" => DocumentType.HuReport, "SP" => DocumentType.SpReport, "tacho_calibration" => DocumentType.TachoCalibration, "insurance" => DocumentType.Insurance, "community_licence" => DocumentType.CommunityLicence, _ => throw new InvalidOperationException("unknown_document_type") };

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
