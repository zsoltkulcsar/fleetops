using FleetOps.Kernel.Authorization;
using FleetOps.Kernel;

namespace FleetOps.Modules.Documents;

public sealed record DriverFile(Guid Id, DocumentFilePage Page, string ContentType, long SizeBytes);
[VisibleTo(RoleCode.Driver)]
public sealed record DriverDocumentDetail(Guid Id, DocumentType Type, DocumentStatus Status, DateOnly? ExpiresAt, string? RejectReason, IReadOnlyList<DriverFile> Files) : IReadModel;
[CommandPolicy("documents.driver.upload")]
public sealed record ConfirmDriverDocumentCommand(Guid? SubjectId, DocumentType Type, string? Number, DateOnly? IssuedAt, DateOnly? ExpiresAt, string[]? Categories, IReadOnlyList<ConfirmFileInput> Files) : ICommand<DriverDocumentDetail>, IHasSubject;
