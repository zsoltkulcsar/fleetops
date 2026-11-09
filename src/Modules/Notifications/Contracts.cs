using System.Text.Json;
using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Notifications;

public enum NotificationChannel { InApp, Email, Push }

public sealed class Notification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public Guid? AlertId { get; set; }
    public string TemplateCode { get; set; } = "";
    public string Parameters { get; set; } = "{}";
    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
}

public interface INotifications
{
    Task<Guid> AddAsync(Guid employeeId, string templateCode, object parameters, Guid? alertId, CommandContext context, CancellationToken cancellationToken);
}

public interface INotificationSender
{
    NotificationChannel Channel { get; }
    Task SendAsync(IReadOnlyCollection<Guid> ids, CommandContext context, CancellationToken cancellationToken);
}

public interface IPostCommitNotifications
{
    Task TrySendAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
public interface INotificationIds
{
    IReadOnlyCollection<Guid> Items { get; }
    void Add(Guid id);
}
public sealed class NotificationIds : INotificationIds
{
    private readonly List<Guid> items = [];
    public IReadOnlyCollection<Guid> Items => items;
    public void Add(Guid id) => items.Add(id);
}

[QueryPolicy("notifications.mine")]
public sealed record ListNotificationsQuery(string? Cursor, int Limit, Guid? SubjectId = null) : IQuery<NotificationListResult>, IHasSubject;
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record NotificationListResult(IReadOnlyList<NotificationItem> Items, int Unread, string? NextCursor) : IReadModel;
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record NotificationItem(Guid Id, string TemplateCode, string Text, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt, string Link);

[CommandPolicy("notifications.mine")]
public sealed record MarkNotificationReadCommand(Guid Id, Guid? SubjectId) : ICommand<bool>, IHasSubject;
[CommandPolicy("notifications.mine")]
public sealed record MarkAllNotificationsReadCommand(Guid? SubjectId) : ICommand<int>, IHasSubject;

public static class NotificationTemplates
{
    public static string Render(string templateCode, IReadOnlyDictionary<string, JsonElement> parameters, string? language)
    {
        var german = string.Equals(language, "de", StringComparison.OrdinalIgnoreCase);
        if (templateCode == "schedule_changed") return german ? "Ihr Dienstplan wurde geändert" : "Your schedule has changed";
        if (IsMedicalDocument(parameters)) return german ? "Bitte Dokumente prüfen" : "Please check your documents";
        if (templateCode == "document_rejected")
        {
            var type = parameters.TryGetValue("documentType", out var value) ? value.GetString() : null;
            var label = DocumentTypeLabel(type, german);
            return german ? $"Dokument {label} wurde abgelehnt" : $"{label} document was rejected";
        }
        return german ? "Kritischer Hinweis erfordert Ihre Aufmerksamkeit" : "A critical alert needs your attention";
    }

    private static bool IsMedicalDocument(IReadOnlyDictionary<string, JsonElement> parameters)
    {
        if (!parameters.TryGetValue("documentType", out var documentType) || !string.Equals(documentType.GetString(), "medical", StringComparison.OrdinalIgnoreCase)) return false;
        return !parameters.TryGetValue("ruleId", out var ruleId) || string.Equals(ruleId.GetString(), "A-02", StringComparison.OrdinalIgnoreCase);
    }

    private static string DocumentTypeLabel(string? value, bool german) => value?.ToLowerInvariant() switch
    {
        "licence_c_ce" => german ? "Führerschein" : "licence",
        "driver_card" => german ? "Fahrerkarte" : "driver card",
        "code95" => "Code 95",
        "medical" => german ? "medizinisches" : "medical",
        "adr_certificate" => german ? "ADR-Zertifikat" : "ADR certificate",
        "id_card" => german ? "Ausweis" : "ID card",
        _ => german ? "Dokument" : "document"
    };
}
