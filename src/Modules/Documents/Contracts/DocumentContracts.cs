using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;
using System.Text.Json.Serialization;

namespace FleetOps.Modules.Documents;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentListItem(Guid Id, DocumentOwnerType OwnerType, Guid OwnerId, string OwnerName, DocumentType Type, string? Number, DateOnly? IssuedAt, DateOnly? ExpiresAt, DocumentStatus Status, DateTimeOffset UploadedAt, int Version);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentFileDetail(Guid Id, DocumentFilePage Page, string ContentType, long SizeBytes);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentDetail(Guid Id, DocumentOwnerType OwnerType, Guid OwnerId, string OwnerName, DocumentType Type, string? Number, DateOnly? IssuedAt, DateOnly? ExpiresAt, DocumentStatus Status, DateTimeOffset UploadedAt, int Version, DateTimeOffset? VerifiedAt, string? RejectReason, IReadOnlyList<string> Categories, IReadOnlyList<DocumentFileDetail> Files) : IReadModel;
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentListResult(IReadOnlyList<DocumentListItem> Items, string? NextCursor) : IReadModel;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PeopleLookupResult(IReadOnlyList<PeopleLookupItem> Items) : IReadModel;
public sealed record PeopleLookupItem(Guid Id, string Name);

[CommandPolicy("documents.driver.upload")]
public sealed record RequestEmployeeDocumentUploadCommand(Guid? SubjectId, DocumentType Type, string ContentType, long SizeBytes) : ICommand<UploadLinkResult>, IHasSubject;
[CommandPolicy("documents.vehicle.manage")]
public sealed record RequestVehicleDocumentUploadCommand(Guid? SubjectId, DocumentType Type, string ContentType, long SizeBytes) : ICommand<UploadLinkResult>, IHasSubject;
[CommandPolicy("documents.driver.upload")]
public sealed record ConfirmEmployeeDocumentCommand(Guid? SubjectId, Guid OwnerId, DocumentType Type, string? Number, DateOnly? IssuedAt, DateOnly? ExpiresAt, string[]? Categories, IReadOnlyList<ConfirmFileInput> Files) : ICommand<DocumentDetail>, IHasSubject;
[CommandPolicy("documents.vehicle.manage")]
public sealed record ConfirmVehicleDocumentCommand(Guid? SubjectId, Guid OwnerId, DocumentType Type, string? Number, DateOnly? IssuedAt, DateOnly? ExpiresAt, string[]? Categories, IReadOnlyList<ConfirmFileInput> Files) : ICommand<DocumentDetail>, IHasSubject;
public sealed record ConfirmFileInput(Guid FileId, DocumentFilePage Page);
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record UploadRequiredHeaders([property: JsonPropertyName("Content-Type")] string ContentType);
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record UploadLinkResult(Guid FileId, ObjectLink Url, DateTimeOffset ExpiresAt, UploadRequiredHeaders RequiredHeaders);

[QueryPolicy("documents.view")]
public sealed record ListDocumentsQuery(DocumentOwnerType? OwnerType, Guid? OwnerId, DocumentStatus? Status, string? Search, string? Cursor, int Limit) : IQuery<DocumentListResult>;
[QueryPolicy("documents.view")]
public sealed record GetDocumentQuery(Guid? SubjectId) : IQuery<DocumentDetail>, IHasSubject;
[QueryPolicy("documents.people.lookup")]
public sealed record LookupPeopleQuery(string? Search) : IQuery<PeopleLookupResult>;
