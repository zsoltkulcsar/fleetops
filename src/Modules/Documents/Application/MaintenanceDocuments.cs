using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Maintenance;
namespace FleetOps.Modules.Documents;

public sealed class MaintenanceDocuments(IDocumentStore store) : IMaintenanceDocuments
{
    public async Task<Guid> CreateVerifiedResultAsync(Guid vehicleId, MaintenanceResultDocument input, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var type = input.Type switch
        {
            "hu_report" => DocumentType.HuReport,
            "sp_report" => DocumentType.SpReport,
            "tacho_calibration" => DocumentType.TachoCalibration,
            "registration" => DocumentType.Registration,
            "insurance" => DocumentType.Insurance,
            "community_licence" => DocumentType.CommunityLicence,
            _ => throw new DocumentValidationException(["resultDocument.type"])
        };
        if (input.Files is null || input.Files.Any(file => file is null)) throw new DocumentValidationException(["resultDocument.files"]);
        var files = input.Files.Select(file => new ConfirmFileInput(file.FileId, file.Page switch
        {
            "front" => DocumentFilePage.Front,
            "back" => DocumentFilePage.Back,
            "page" => DocumentFilePage.Page,
            _ => throw new DocumentValidationException(["resultDocument.files.page"])
        })).ToArray();
        var document = await store.ConfirmAsync(vehicleId, DocumentOwnerType.Vehicle, type, input.Number, input.IssuedAt, input.ExpiresAt, null, files, actor, context, cancellationToken);
        var verified = await store.VerifyAsync(document.Id, document.Version, input.ExpiresAt, input.IssuedAt, input.Number, null, actor, context, cancellationToken);
        return verified.Id;
    }
}
