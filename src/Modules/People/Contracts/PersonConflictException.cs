using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public sealed class PersonConflictException(string code, IReadOnlyList<string>? types = null) : Exception(code)
{
    public string Code { get; } = code;
    public IReadOnlyList<string> Types { get; } = types ?? [];
}
