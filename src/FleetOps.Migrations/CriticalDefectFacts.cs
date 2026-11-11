using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Fleet;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfCriticalDefectFacts : ICriticalDefectFacts
{
    public async Task<bool> IsOpenCriticalAsync(Guid defectId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        return await db.DefectReports.AnyAsync(defect => defect.Id == defectId && defect.Severity == DefectSeverity.Critical &&
            (defect.Status == DefectStatus.Open || defect.Status == DefectStatus.Acknowledged || defect.Status == DefectStatus.InRepair), cancellationToken);
    }
}
