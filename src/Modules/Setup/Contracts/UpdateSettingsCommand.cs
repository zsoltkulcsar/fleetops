using System.Text.Json;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
[CommandPolicy("settings.edit")]
public sealed record UpdateSettingsCommand(IReadOnlyDictionary<string, JsonElement>? Changes) : ICommand<SettingsView>;
