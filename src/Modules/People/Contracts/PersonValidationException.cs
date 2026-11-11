using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public sealed class PersonValidationException(IReadOnlyList<string> fields) : Exception("validation_error")
{
    public IReadOnlyList<string> Fields { get; } = fields;
}
