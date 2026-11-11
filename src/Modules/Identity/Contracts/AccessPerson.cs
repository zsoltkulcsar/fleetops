using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
public sealed record AccessPerson(Guid Id, string? Phone, string Email, string[] Roles, string? PersonnelNumber);
