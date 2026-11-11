using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
public sealed class AbsenceConflictException(string code, IReadOnlyList<AbsenceShift>? shifts = null) : Exception(code)
{
    public string Code { get; } = code;
    public IReadOnlyList<AbsenceShift> Shifts { get; } = shifts ?? [];
}
