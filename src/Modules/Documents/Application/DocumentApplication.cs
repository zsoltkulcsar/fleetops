using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Documents;

public interface IDocumentStore
{
    Task<UploadLinkResult> RequestAsync(Guid ownerId, DocumentOwnerType ownerType, DocumentType type, string contentType, long sizeBytes, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task<DocumentDetail> ConfirmAsync(Guid ownerId, DocumentOwnerType ownerType, DocumentType type, string? number, DateOnly? issuedAt, DateOnly? expiresAt, string[]? categories, IReadOnlyList<ConfirmFileInput> files, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
public sealed class RequestEmployeeDocumentUploadHandler(IDocumentStore store) : ICommandHandler<RequestEmployeeDocumentUploadCommand, UploadLinkResult>
{ public Task<UploadLinkResult> HandleAsync(RequestEmployeeDocumentUploadCommand c, AccessContext a, CommandContext x, CancellationToken t) => store.RequestAsync(c.SubjectId ?? throw new DocumentValidationException(["ownerId"]), DocumentOwnerType.Employee, c.Type, c.ContentType, c.SizeBytes, a, x, t); }
public sealed class RequestVehicleDocumentUploadHandler(IDocumentStore store) : ICommandHandler<RequestVehicleDocumentUploadCommand, UploadLinkResult>
{ public Task<UploadLinkResult> HandleAsync(RequestVehicleDocumentUploadCommand c, AccessContext a, CommandContext x, CancellationToken t) => store.RequestAsync(c.SubjectId ?? throw new DocumentValidationException(["ownerId"]), DocumentOwnerType.Vehicle, c.Type, c.ContentType, c.SizeBytes, a, x, t); }
public sealed class ConfirmEmployeeDocumentHandler(IDocumentStore store) : ICommandHandler<ConfirmEmployeeDocumentCommand, DocumentDetail>
{ public Task<DocumentDetail> HandleAsync(ConfirmEmployeeDocumentCommand c, AccessContext a, CommandContext x, CancellationToken t) => store.ConfirmAsync(c.OwnerId, DocumentOwnerType.Employee, c.Type, c.Number, c.IssuedAt, c.ExpiresAt, c.Categories, c.Files, a, x, t); }
public sealed class ConfirmVehicleDocumentHandler(IDocumentStore store) : ICommandHandler<ConfirmVehicleDocumentCommand, DocumentDetail>
{ public Task<DocumentDetail> HandleAsync(ConfirmVehicleDocumentCommand c, AccessContext a, CommandContext x, CancellationToken t) => store.ConfirmAsync(c.OwnerId, DocumentOwnerType.Vehicle, c.Type, c.Number, c.IssuedAt, c.ExpiresAt, c.Categories, c.Files, a, x, t); }
public sealed class DocumentValidationException(IReadOnlyList<string> fields, string code = "validation_error") : Exception(code) { public IReadOnlyList<string> Fields { get; } = fields; public string Code { get; } = code; }
public sealed class DocumentConflictException(string code) : Exception(code) { public string Code { get; } = code; }
