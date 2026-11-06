namespace FleetOps.Api.Auth;

public interface IMailbox
{
    void Store(string address, string link);
}

public sealed class DemoMailbox(IHostEnvironment environment, ILogger<DemoMailbox> logger) : IMailbox
{
    public string? LastLink { get; private set; }
    public void Store(string address, string link)
    {
        if (environment.IsProduction())
        {
            throw new InvalidOperationException("mailbox_unavailable");
        }
        LastLink = link;
        logger.LogInformation("Demo password reset message queued");
    }
}
