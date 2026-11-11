namespace FleetOps.People;

public interface IPersonHistoryStore
{
    Task<PersonHistory> ReadAsync(Guid personId, CancellationToken cancellationToken);
}
