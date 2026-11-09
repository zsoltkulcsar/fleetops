namespace FleetOps.Identity;

// Authentication uses a dedicated anonymous CommandDispatcher overload.
public sealed record EnrolDriverCommand(string? PersonnelNumber, string? Code);
