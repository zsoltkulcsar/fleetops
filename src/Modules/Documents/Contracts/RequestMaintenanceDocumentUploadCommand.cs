using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Documents;

[CommandPolicy("maintenance.manage")]
public sealed record RequestMaintenanceDocumentUploadCommand(Guid VehicleId, DocumentType Type, string ContentType, long SizeBytes) : ICommand<UploadLinkResult>;
