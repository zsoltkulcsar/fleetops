using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Payroll;

public sealed class PayrollQueries(IPayrollStore store) :
    IQueryHandler<GetPayrollReadinessQuery, PayrollReadiness>, IQueryHandler<PreviewPayrollQuery, PayrollPreview>,
    IQueryHandler<GetPayrollHistoryQuery, PayrollHistory>
{
    public async Task<PayrollReadiness> HandleAsync(GetPayrollReadinessQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        PayrollViews.ValidateMonth(query.Month);
        return new(query.Month, (await store.ReadAsync(query.Month, null, cancellationToken)).People);
    }

    public async Task<PayrollPreview> HandleAsync(PreviewPayrollQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        PayrollViews.ValidateMonth(query.Month);
        return PayrollViews.Preview(query.Month, await store.ReadAsync(query.Month, null, cancellationToken));
    }

    public async Task<PayrollHistory> HandleAsync(GetPayrollHistoryQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        PayrollViews.ValidateMonth(query.Month);
        return new((await store.HistoryAsync(query.Month, null, cancellationToken)).Select(PayrollViews.From).ToArray());
    }
}
