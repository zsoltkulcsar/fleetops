using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
public sealed class AccessValidationException(IReadOnlyList<string> fields) : Exception("validation_error")
{
    public IReadOnlyList<string> Fields { get; } = fields;
}
