using System.Security.Cryptography;
using System.Text;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;
namespace FleetOps.Identity;
public sealed class EnrolDriverHandler(IEnrolmentStore store, ISettings settings, TimeProvider time, ICompanyTimeService clock)
{
    public async Task<EnrolDriverResult?> ExecuteAsync(EnrolDriverCommand command, CommandContext context, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var maxTries = await EnrolmentRules.PositiveSetting(settings, "enrolment.max_tries", context, ct);
        var codes = await store.ActiveCodesAsync(now, maxTries, context, ct);
        var wellFormed = command.Code is { Length: 6 } && command.Code.All(character => character is >= '0' and <= '9')
            && command.PersonnelNumber is { Length: > 0 and <= 100 };
        // Hash and compare the entire candidate set on every path, including malformed input.
        var suppliedCode = command.Code is { Length: <= 100 } ? command.Code : "invalid";
        var dummyHash = SessionTokens.Hash($"{Guid.Empty}:{suppliedCode}");
        _ = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(dummyHash), Encoding.UTF8.GetBytes(SessionTokens.Hash($"{Guid.Empty}:000000")));
        var matches = new List<EnrolmentCode>();
        foreach (var candidate in codes)
        {
            var hash = SessionTokens.Hash($"{candidate.EmployeeId}:{suppliedCode}");
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hash), Encoding.UTF8.GetBytes(candidate.CodeHash)))
                matches.Add(candidate);
        }
        // Acquire employee locks in a stable order before updating any code rows.
        var employees = new Dictionary<Guid, EnrolmentEmployee?>();
        foreach (var employeeId in codes.Select(candidate => candidate.EmployeeId).Distinct().Order())
            employees[employeeId] = await store.LockEmployeeAsync(employeeId, context, ct);
        EnrolmentCode? selected = null;
        foreach (var candidate in codes)
        {
            var employee = employees[candidate.EmployeeId];
            var rightNumber = employee?.PersonnelNumber is not null && employee.PersonnelNumber == command.PersonnelNumber;
            var rightCode = matches.Contains(candidate);
            if (wellFormed && rightNumber && rightCode && EnrolmentRules.Eligible(employee, now, clock))
                selected = candidate;
            else if (rightNumber || rightCode)
                await store.IncrementAttemptsAsync(candidate.Id, now, maxTries, context, ct);
        }
        if (selected is null)
            return null;
        // Serialize with replacement sends and recheck eligibility after acquiring the lock.
        var lockedEmployee = await store.LockEmployeeAsync(selected.EmployeeId, context, ct);
        now = time.GetUtcNow();
        if (!EnrolmentRules.Eligible(lockedEmployee, now, clock) || lockedEmployee!.PersonnelNumber != command.PersonnelNumber
            || !await store.ConsumeAsync(selected.Id, now, maxTries, context, ct))
            return null;
        var days = await EnrolmentRules.PositiveSetting(settings, "driver.session_days", context, ct);
        var userId = await store.FindPasswordlessUserAsync(selected.EmployeeId, context, ct);
        if (userId is null)
        {
            var name = $"driver-{selected.EmployeeId:N}";
            var user = new OfficeUser
            {
                Id = Guid.NewGuid(),
                EmployeeId = selected.EmployeeId,
                UserName = name,
                NormalizedUserName = new Microsoft.AspNetCore.Identity.UpperInvariantLookupNormalizer().NormalizeName(name),
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                LockoutEnabled = true
            };
            await store.AddUserAsync(user, context, ct);
            userId = user.Id;
        }
        var stage = await store.HasPrivacyAsync(selected.EmployeeId, context, ct) ? SessionStage.Full : SessionStage.PendingPrivacy;
        var token = SessionTokens.New();
        await store.AddSessionAsync(new OfficeSession
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            EmployeeId = selected.EmployeeId,
            TokenHash = SessionTokens.Hash(token),
            Device = "driver",
            Stage = stage,
            CreatedAt = now,
            StageChangedAt = now,
            LastSeenAt = now
        }, context, ct);
        return new(stage, token, days);
    }
}
