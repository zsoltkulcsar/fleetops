using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
[CommandPolicy("employees.phone.change")]
public sealed record ChangePersonPhoneCommand(Guid PersonId, string? Phone, string? Reason) : ICommand<PhoneChangeResult>;
