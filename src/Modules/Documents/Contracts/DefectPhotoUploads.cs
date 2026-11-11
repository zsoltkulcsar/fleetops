using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Documents;

[CommandPolicy("shifts.enter_edit", HasStatePrecondition = true)]
public sealed record RequestDefectPhotoUploadCommand(Guid? SubjectId, string ContentType, long SizeBytes) : ICommand<UploadLinkResult>, IHasSubject;

public interface IDefectPhotoUploads
{
    Task<UploadLinkResult> RequestAsync(string contentType, long sizeBytes, Guid actorId, CommandContext context, CancellationToken cancellationToken);
}
