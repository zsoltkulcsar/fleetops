using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Audit;
using System.Text.Json.Serialization;

namespace FleetOps.Modules.Documents;

public enum DocumentOwnerType { Employee, Vehicle }
public enum DocumentType { [JsonStringEnumMemberName("licence_c_ce")] LicenceC_CE, DriverCard, Code95, Medical, AdrCertificate, IdCard, EmploymentContract, Registration, HuReport, SpReport, TachoCalibration, Insurance, CommunityLicence }
public enum DocumentStatus { Uploaded, Verified, Rejected, Replaced }
public enum DocumentFilePage { Front, Back, Page }
public enum IssuedReason { Renewal, ReplacementLost, ClassChange }

public sealed record DocumentTypeCatalog(DocumentType Type, DocumentOwnerType OwnerType, bool HasExpiry, bool TwoSided, bool Blocking, bool TruckOnly, int? DefaultLeadDays);

public static class DocumentsCatalog
{
    public static readonly IReadOnlyList<DocumentTypeCatalog> All =
    [
        new(DocumentType.LicenceC_CE, DocumentOwnerType.Employee, true, true, true, false, 60),
        new(DocumentType.DriverCard, DocumentOwnerType.Employee, true, true, true, false, 60),
        new(DocumentType.Code95, DocumentOwnerType.Employee, true, false, true, false, 90),
        new(DocumentType.Medical, DocumentOwnerType.Employee, true, false, true, false, 60),
        new(DocumentType.AdrCertificate, DocumentOwnerType.Employee, true, false, false, false, 60),
        new(DocumentType.IdCard, DocumentOwnerType.Employee, true, false, false, false, 30),
        new(DocumentType.EmploymentContract, DocumentOwnerType.Employee, false, false, false, false, null),
        new(DocumentType.Registration, DocumentOwnerType.Vehicle, false, false, true, false, null),
        new(DocumentType.HuReport, DocumentOwnerType.Vehicle, true, false, true, false, 30),
        new(DocumentType.SpReport, DocumentOwnerType.Vehicle, true, false, true, false, 30),
        new(DocumentType.TachoCalibration, DocumentOwnerType.Vehicle, true, false, true, true, 30),
        new(DocumentType.Insurance, DocumentOwnerType.Vehicle, true, false, true, false, 30),
        new(DocumentType.CommunityLicence, DocumentOwnerType.Vehicle, true, false, true, true, 60)
    ];
    public static DocumentTypeCatalog Get(DocumentType type) => All.Single(x => x.Type == type);
}

[AuditedEntity]
public sealed class Document
{
    [NotAudited] public Guid Id { get; set; } = Guid.CreateVersion7();
    [AuditField(AuditFieldClasses.Plain)] public DocumentOwnerType OwnerType { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid OwnerId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DocumentType Type { get; set; }
    [AuditField(AuditFieldClasses.Personal)] public string? Number { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly? IssuedAt { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly? ExpiresAt { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;
    [AuditField(AuditFieldClasses.Plain)] public Guid? VerifiedBy { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateTimeOffset? VerifiedAt { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public string? RejectReason { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid? ReplacesId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public string[] Categories { get; set; } = [];
    [AuditField(AuditFieldClasses.Plain)] public DateOnly? LastSightCheckAt { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public IssuedReason? IssuedReason { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public bool ImportAttested { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly? RetainUntil { get; set; }
    [NotAudited] public Guid CreatedBy { get; set; }
    [NotAudited] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [NotAudited] public int Version { get; set; } = 1;
    public List<DocumentFile> Files { get; set; } = [];
}

public sealed class DocumentFile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DocumentId { get; set; }
    public string ObjectKey { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DocumentFilePage Page { get; set; }
    public int SortOrder { get; set; }
}

public sealed class PendingFile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string ObjectKey { get; set; } = "";
    public Guid ActorId { get; set; }
    public string Purpose { get; set; } = "document";
    public string ContentType { get; set; } = "";
    public long MaxSizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}
