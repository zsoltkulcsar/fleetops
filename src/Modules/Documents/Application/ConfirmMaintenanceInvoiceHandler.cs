using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Documents;

public sealed class ConfirmMaintenanceInvoiceHandler(IDocumentStore store) : ICommandHandler<ConfirmMaintenanceInvoiceCommand, DocumentDetail>
{
    public Task<DocumentDetail> HandleAsync(ConfirmMaintenanceInvoiceCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (command.Files is null || command.Files.Any(file => file is null)) throw new DocumentValidationException(["files"]);
        return store.ConfirmAsync(command.VehicleId, DocumentOwnerType.Vehicle, DocumentType.MaintenanceInvoice, null, null, null, null, command.Files, actor, context, cancellationToken);
    }
}
