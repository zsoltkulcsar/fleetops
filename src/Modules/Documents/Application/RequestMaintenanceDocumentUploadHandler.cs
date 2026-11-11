using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Documents;

public sealed class RequestMaintenanceDocumentUploadHandler(IDocumentStore store) : ICommandHandler<RequestMaintenanceDocumentUploadCommand, UploadLinkResult>
{
    public Task<UploadLinkResult> HandleAsync(RequestMaintenanceDocumentUploadCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Type) || DocumentsCatalog.Get(command.Type).OwnerType != DocumentOwnerType.Vehicle)
            throw new DocumentValidationException(["type"]);
        return store.RequestAsync(command.VehicleId, DocumentOwnerType.Vehicle, command.Type, command.ContentType, command.SizeBytes, actor, context, cancellationToken);
    }
}
