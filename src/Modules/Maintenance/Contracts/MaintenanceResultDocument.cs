namespace FleetOps.Modules.Maintenance;

public sealed record MaintenanceResultDocument(string Type, DateOnly? IssuedAt, DateOnly? ExpiresAt, string? Number, IReadOnlyList<MaintenanceDocumentFile> Files);
