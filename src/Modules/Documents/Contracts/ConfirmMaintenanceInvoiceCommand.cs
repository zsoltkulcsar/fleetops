using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Documents;

[CommandPolicy("maintenance.manage")]
public sealed record ConfirmMaintenanceInvoiceCommand(Guid VehicleId, IReadOnlyList<ConfirmFileInput> Files) : ICommand<DocumentDetail>;
