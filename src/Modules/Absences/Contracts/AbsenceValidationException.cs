using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
public sealed class AbsenceValidationException(params string[] fields) : Exception("validation_error")
{
    public IReadOnlyList<string> Fields { get; } = fields;
}
