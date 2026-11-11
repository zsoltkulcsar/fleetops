using System.Text.Json;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
[CommandPolicy("settings.holidays")]
public sealed record RemoveHolidayCommand(DateOnly Date) : ICommand<SettingsView>;
