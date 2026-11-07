namespace FleetOps.Kernel;

public interface IChangedSubjects
{
    void Add(string subjectType, Guid id);
    IReadOnlyCollection<(string SubjectType, Guid Id)> Items { get; }
}

public sealed class ChangedSubjects : IChangedSubjects
{
    private readonly HashSet<(string SubjectType, Guid Id)> items = [];
    public IReadOnlyCollection<(string SubjectType, Guid Id)> Items => items;
    public void Add(string subjectType, Guid id) => items.Add((subjectType, id));
}

public interface IPostCommandEvaluation
{
    Task EvaluateAsync(IReadOnlyCollection<(string SubjectType, Guid Id)> subjects, FleetOps.Kernel.Authorization.CommandContext context, CancellationToken cancellationToken);
}
