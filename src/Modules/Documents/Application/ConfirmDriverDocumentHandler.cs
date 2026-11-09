using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Documents;

public sealed class ConfirmDriverDocumentHandler(IDocumentStore store) : ICommandHandler<ConfirmDriverDocumentCommand, DriverDocumentDetail>
{
    public async Task<DriverDocumentDetail> HandleAsync(ConfirmDriverDocumentCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (command.Files is null) throw new DocumentValidationException(["files"]);
        var detail = await store.ConfirmAsync(command.SubjectId ?? throw new DocumentValidationException(["employeeId"]), DocumentOwnerType.Employee, command.Type, command.Number, command.IssuedAt, command.ExpiresAt, command.Categories, command.Files, actor, context, cancellationToken);
        return new(detail.Id, detail.Type, detail.Status, detail.ExpiresAt, detail.RejectReason, detail.Files.Select(file => new DriverFile(file.Id, file.Page, file.ContentType, file.SizeBytes)).ToArray());
    }
}
