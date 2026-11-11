using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Documents;

public sealed class RequestOfficeDefectPhotoUploadHandler(IDefectPhotoUploads uploads) : ICommandHandler<RequestOfficeDefectPhotoUploadCommand, UploadLinkResult>
{
    public Task<UploadLinkResult> HandleAsync(RequestOfficeDefectPhotoUploadCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        => uploads.RequestAsync(command.ContentType, command.SizeBytes, actor.EmployeeId, context, cancellationToken);
}
