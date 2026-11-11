using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public sealed record PersonFacts(Employee Employee, IReadOnlyList<PersonDocumentFact> Documents,
    bool CanDrive, IReadOnlyList<string> ReasonCategories, string? DefaultVehiclePlate);
