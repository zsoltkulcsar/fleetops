using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;
using FleetOps.Modules.Fleet;
using System.Text.Json.Serialization;

namespace FleetOps.Modules.Documents;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentListItem(Guid Id, DocumentOwnerType OwnerType, Guid OwnerId, string OwnerName, DocumentType Type, string? Number, DateOnly? IssuedAt, DateOnly? ExpiresAt, DocumentStatus Status, DateTimeOffset UploadedAt, int Version, bool CanAct);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentFileDetail(Guid Id, DocumentFilePage Page, string ContentType, long SizeBytes);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentDetail(Guid Id, DocumentOwnerType OwnerType, Guid OwnerId, string OwnerName, DocumentType Type, string? Number, DateOnly? IssuedAt, DateOnly? ExpiresAt, DocumentStatus Status, DateTimeOffset UploadedAt, int Version, DateTimeOffset? VerifiedAt, string? RejectReason, IReadOnlyList<string> Categories, IReadOnlyList<DocumentFileDetail> Files, bool CanAct) : IReadModel;
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentListResult(IReadOnlyList<DocumentListItem> Items, string? NextCursor) : IReadModel;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PeopleLookupResult(IReadOnlyList<PeopleLookupItem> Items) : IReadModel;
public sealed record PeopleLookupItem(Guid Id, string Name);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentCounts(int Queue, int Expiring, int Expired, int Missing) : IReadModel;
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentMissingItem(DocumentOwnerType OwnerType, Guid OwnerId, string OwnerName, DocumentType DocumentType);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentMissingResult(IReadOnlyList<DocumentMissingItem> Items, string? NextCursor) : IReadModel;
 [VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentSearchResult(IReadOnlyList<DocumentSearchItem> Documents, IReadOnlyList<VehicleSearchItem> Vehicles, IReadOnlyList<PeopleSearchItem> People) : IReadModel;
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentSearchItem(Guid Id, DocumentType Type, string? Number, string OwnerName);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record VehicleSearchItem(Guid Id, string Plate, string Make, string Model);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record PeopleSearchItem(Guid Id, string Name);
 [VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DocumentFileLinkResult(ObjectLink Link) : IReadModel;

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
public sealed record ListDocumentsQuery(DocumentOwnerType? OwnerType, Guid? OwnerId, DocumentStatus? Status, string? Search, string? Cursor, int Limit, string? Filter = null) : IQuery<DocumentListResult>;
[QueryPolicy("documents.view")]
public sealed record GetDocumentQuery(Guid? SubjectId) : IQuery<DocumentDetail>, IHasSubject;
[QueryPolicy("documents.view")]
public sealed record GetDocumentCountsQuery : IQuery<DocumentCounts>;
[QueryPolicy("documents.view")]
public sealed record ListMissingDocumentsQuery(string? Cursor, int Limit) : IQuery<DocumentMissingResult>;
[QueryPolicy("documents.view")]
public sealed record SearchDocumentsQuery(string? Search) : IQuery<DocumentSearchResult>;
[QueryPolicy("documents.view")]
public sealed record GetDocumentFileLinkQuery(Guid DocumentId, Guid FileId) : IQuery<DocumentFileLinkResult>;
[QueryPolicy("documents.people.lookup")]
public sealed record LookupPeopleQuery(string? Search, Guid? Id) : IQuery<PeopleLookupResult>;

[CommandPolicy("documents.driver.verify")]
public sealed record VerifyEmployeeDocumentCommand(Guid DocumentId, int Version, DateOnly? ExpiresAt, DateOnly? IssuedAt, string? Number, string[]? Categories) : ICommand<DocumentDetail>;
[CommandPolicy("documents.vehicle.manage")]
public sealed record VerifyVehicleDocumentCommand(Guid DocumentId, int Version, DateOnly? ExpiresAt, DateOnly? IssuedAt, string? Number, string[]? Categories) : ICommand<DocumentDetail>;
[CommandPolicy("documents.driver.verify")]
public sealed record RejectEmployeeDocumentCommand(Guid DocumentId, int Version, string Reason) : ICommand<DocumentDetail>, IHasReason;
[CommandPolicy("documents.vehicle.manage")]
public sealed record RejectVehicleDocumentCommand(Guid DocumentId, int Version, string Reason) : ICommand<DocumentDetail>, IHasReason;
[CommandPolicy("documents.driver.delete")]
public sealed record DeleteDocumentCommand(Guid DocumentId, int Version, string Reason) : ICommand<DocumentDetail>, IHasReason;
[CommandPolicy("documents.driver.verify")]
public sealed record SightCheckDocumentCommand(Guid DocumentId, int Version, DateOnly CheckedAt) : ICommand<DocumentDetail>;
