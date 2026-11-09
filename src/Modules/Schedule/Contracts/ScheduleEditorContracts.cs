using FleetOps.Kernel.Authorization;
using FleetOps.Kernel;
using FleetOps.People;

namespace FleetOps.Modules.Schedule;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record CycleDetail(Guid Id, Guid EmployeeId, string EmployeeName, Guid VehicleId, string VehiclePlate, DateOnly From, DateOnly To, CycleStatus Status, CycleSource Source, int? PatternSlot, int Version, bool HasClash = false) : IReadModel;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ScheduleResult(DateTimeOffset GeneratedAt, IReadOnlyList<CycleDetail> Items) : IReadModel;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record CyclePatternView(Guid EmployeeId, int Version, CyclePattern? Pattern) : IReadModel;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record VehicleRetirementImpact(IReadOnlyList<CycleDetail> Items) : IReadModel;

[QueryPolicy("schedule.view")]
public sealed record GetScheduleQuery(DateOnly From, DateOnly To, Guid? EmployeeId, Guid? VehicleId) : IQuery<ScheduleResult>;
[QueryPolicy("schedule.view")]
public sealed record GetCyclePatternQuery(Guid EmployeeId) : IQuery<CyclePatternView>;
[QueryPolicy("vehicles.view")]
public sealed record GetVehicleRetirementImpactQuery(Guid VehicleId) : IQuery<VehicleRetirementImpact>;

[CommandPolicy("schedule.edit")]
public sealed record AddManualCycleCommand(Guid EmployeeId, Guid VehicleId, DateOnly From, DateOnly To, string Reason) : ICommand<CycleDetail>;
[CommandPolicy("schedule.edit")]
public sealed record EditCycleCommand(Guid Id, int Version, Guid EmployeeId, Guid VehicleId, DateOnly From, DateOnly To, string Reason) : ICommand<CycleDetail>;

public sealed record CycleMutation(CycleDetail Detail, Guid? PreviousEmployeeId);
public interface IScheduleEditor
{
    Task<CycleMutation> AddAsync(AddManualCycleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task<CycleMutation> EditAsync(EditCycleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
public interface IScheduleChanged
{
    Task NotifyAsync(CycleMutation mutation, CommandContext context, CancellationToken cancellationToken);
}
