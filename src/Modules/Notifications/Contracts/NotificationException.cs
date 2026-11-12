namespace FleetOps.Modules.Notifications;
public sealed class NotificationException(string code, params string[] fields) : Exception(code)
{
    public string Code { get; } = code;
    public IReadOnlyList<string> Fields { get; } = fields;
}
