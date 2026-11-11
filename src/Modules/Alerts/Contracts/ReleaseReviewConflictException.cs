using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public sealed class ReleaseReviewConflictException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
