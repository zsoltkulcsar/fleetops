using Microsoft.AspNetCore.Identity;

namespace FleetOps.Identity;

public enum SessionStage { PendingSecondFactor, PendingEnrolment, PendingPrivacy, Full }

public sealed class OfficeUser : IdentityUser<Guid>
{
    public Guid EmployeeId { get; set; }
    public long? LastAcceptedTotpStep { get; set; }
    public int FailedSecondFactorAttempts { get; set; }
}

public sealed class OfficeSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid EmployeeId { get; set; }
    public string TokenHash { get; set; } = "";
    public string Device { get; set; } = "";
    public SessionStage Stage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset StageChangedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }
    public DateTimeOffset? PinResetRequestedAt { get; set; }
    public DateTimeOffset? StepUpAt { get; set; }
    public int FailedSecondFactorAttempts { get; set; }
}

public sealed class PrivacyAcknowledgement
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string Version { get; set; } = "";
    public DateTimeOffset AcknowledgedAt { get; set; }
}

public sealed class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
