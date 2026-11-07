namespace FleetOps.Migrations;

public enum JobTrigger
{
    scheduler,
    admin
}

public enum JobStatus
{
    Running,
    Succeeded,
    Partial,
    Failed,
    Skipped
}

public sealed class JobRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public JobTrigger Trigger { get; set; }
    public string ActorName { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public JobStatus Status { get; set; }
    public int Processed { get; set; }
    public string? ErrorType { get; set; }
}

public sealed class JobCheckpoint
{
    public string Name { get; set; } = "";
    public string Cursor { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
