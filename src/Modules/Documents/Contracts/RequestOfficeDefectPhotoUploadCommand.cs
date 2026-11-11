using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Documents;

[CommandPolicy("defects.manage")]
public sealed record RequestOfficeDefectPhotoUploadCommand(string ContentType, long SizeBytes) : ICommand<UploadLinkResult>;
