namespace FleetOps.Api.Auth;

public sealed class AuthOptions
{
    public TimeSpan IdleSessionLifetime { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan AbsoluteSessionLifetime { get; set; } = TimeSpan.FromHours(12);
    public TimeSpan LockoutLifetime { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan StepUpLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan ResetTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
}
