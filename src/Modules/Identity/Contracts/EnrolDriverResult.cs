namespace FleetOps.Identity;

// Internal result: the token is written only to the secure cookie.
public sealed record EnrolDriverResult(SessionStage Stage, string Token, int SessionDays);
