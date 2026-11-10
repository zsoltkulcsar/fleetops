using FleetOps.Kernel.Authorization;
using FleetOps.Kernel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FleetOps.Modules.Documents;

public sealed class ConfirmDriverDocumentHandler(IDocumentStore store, IOfflineCommandResults results) : ICommandHandler<ConfirmDriverDocumentCommand, DriverDocumentDetail>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) } };
    public async Task<DriverDocumentDetail> HandleAsync(ConfirmDriverDocumentCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (command.Files is null) throw new DocumentValidationException(["files"]);
        if (command.CommandId == Guid.Empty) throw new DocumentValidationException(["commandId"]);
        if (command.SchemaVersion != 1) throw new DocumentValidationException(["schemaVersion"], "unsupported_schema_version");
        if (command.CommandId is Guid replayId)
        {
            await results.LockAsync(actor.EmployeeId, context, cancellationToken);
            var prior = await results.ReadAsync(actor.EmployeeId, replayId, context, cancellationToken);
            if (prior is not null)
            {
                if (prior.Type != "document.upload") throw new DocumentConflictException("command_id_reused");
                return JsonSerializer.Deserialize<DriverDocumentDetail>(prior.Result, JsonOptions) ?? throw new InvalidOperationException("invalid_command_receipt");
            }
        }
        var detail = await store.ConfirmAsync(command.SubjectId ?? throw new DocumentValidationException(["employeeId"]), DocumentOwnerType.Employee, command.Type, command.Number, command.IssuedAt, command.ExpiresAt, command.Categories, command.Files, actor, context, cancellationToken, command.CommandId);
        var response = new DriverDocumentDetail(detail.Id, detail.Type, detail.Status, detail.ExpiresAt, detail.RejectReason, detail.Files.Select(file => new DriverFile(file.Id, file.Page, file.ContentType, file.SizeBytes)).ToArray());
        if (command.CommandId is Guid commandId)
            await results.SaveAsync(actor.EmployeeId, commandId, detail.Id, "document.upload", JsonSerializer.Serialize(new { command.Type, command.Files }, JsonOptions), JsonSerializer.Serialize(response, JsonOptions), command.DeviceTime, context, cancellationToken);
        return response;
    }
}
