using System.Text.Json;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
[QueryPolicy("settings.view")]
public sealed record GetSettingsQuery : IQuery<SettingsView>;
