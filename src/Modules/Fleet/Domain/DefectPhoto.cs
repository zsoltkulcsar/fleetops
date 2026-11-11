using FleetOps.Modules.Audit;

namespace FleetOps.Modules.Fleet;

public sealed class DefectPhoto
{
    [NotAudited] public Guid Id { get; set; }
    public Guid DefectId { get; set; }
    public string ObjectKey { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public int SortOrder { get; set; }
}
