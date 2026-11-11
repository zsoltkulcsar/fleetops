using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Fleet;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;

public sealed class EfMajorDefectFacts : IMajorDefectFacts
{
    public async Task<DateTimeOffset?> OpenUnlinkedMajorReportedAtAsync(Guid defectId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        return await db.DefectReports.AsNoTracking().Where(defect => defect.Id == defectId && defect.Severity == DefectSeverity.Major &&
            (defect.Status == DefectStatus.Open || defect.Status == DefectStatus.Acknowledged) && defect.RepairId == null)
            .Select(defect => (DateTimeOffset?)defect.ReportedAt).SingleOrDefaultAsync(cancellationToken);
    }
}
