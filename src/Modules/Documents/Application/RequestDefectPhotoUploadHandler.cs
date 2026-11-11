using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Documents;

public sealed class RequestDefectPhotoUploadHandler(IDefectPhotoUploads uploads) : ICommandHandler<RequestDefectPhotoUploadCommand, UploadLinkResult>
{
    public Task<UploadLinkResult> HandleAsync(RequestDefectPhotoUploadCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        => uploads.RequestAsync(command.ContentType, command.SizeBytes, actor.EmployeeId, context, cancellationToken);
}
