using FleetOps.Modules.Shifts;
namespace FleetOps.Api;
public sealed class GuidanceFailureLog(ILogger<GuidanceFailureLog> logger) : IGuidanceFailureLog
{
    public void Failed(string exceptionType) => logger.LogError("Shift guidance failed. ExceptionType={ExceptionType}", exceptionType);
}
