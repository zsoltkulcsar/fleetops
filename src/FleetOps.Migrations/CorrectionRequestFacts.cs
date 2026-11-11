using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfCorrectionRequestFacts : ICorrectionRequestFacts
{
    public async Task<CorrectionRequestFact?> ReadAsync(Guid requestId, CommandContext context, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        await using var db = new FleetOpsDbContext(options.Options);
        db.Database.UseTransaction(context.DbTransaction);
        return await db.CorrectionRequests.Where(request => request.Id == requestId)
            .Select(request => new CorrectionRequestFact(request.Status == "open", request.RequiresHr)).SingleOrDefaultAsync(cancellationToken);
    }
}
