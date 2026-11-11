using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
public interface ICurrentSession
{
    Guid? SessionId { get; }
}
