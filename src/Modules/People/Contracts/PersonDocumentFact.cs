using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public sealed record PersonDocumentFact(string Type, string Status, DateOnly? ExpiresAt, DateTimeOffset CreatedAt, int Version);
