using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record AbsenceView(Guid Id, Guid EmployeeId, DateOnly From, DateOnly To, HalfDay? HalfDay, AbsenceType Type, AbsenceStatus Status, string? Note, string? DeclineReason, string? CancelReason) : IReadModel
{
    public decimal WorkingDays { get; init; }
}
