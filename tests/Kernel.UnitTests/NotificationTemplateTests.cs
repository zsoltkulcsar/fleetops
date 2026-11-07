using System.Text.Json;
using FleetOps.Modules.Notifications;
using Xunit;

namespace FleetOps.Tests;

public sealed class NotificationTemplateTests
{
    [Fact]
    public void Template_renders_in_english()
    {
        Assert.Equal("licence document was rejected", Render("document_rejected", "en", ("documentType", "licence_c_ce")));
    }

    [Fact]
    public void Template_renders_in_german()
    {
        Assert.Equal("Dokument Führerschein wurde abgelehnt", Render("document_rejected", "de", ("documentType", "licence_c_ce")));
    }

    [Fact]
    public void Unknown_language_falls_back_to_english()
    {
        Assert.Equal(Render("critical_alert", "en"), Render("critical_alert", "fr"));
    }

    [Fact]
    public void Medical_document_renders_the_generic_text()
    {
        var english = Render("critical_alert", "en", ("documentType", "medical"), ("ruleId", "A-02"));
        var german = Render("critical_alert", "de", ("documentType", "medical"), ("ruleId", "A-02"));

        Assert.Equal("Please check your documents", english);
        Assert.Equal("Bitte Dokumente prüfen", german);
        Assert.DoesNotContain("medical", english, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("medizin", german, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_template_renders_in_every_language()
    {
        var templateCodes = new[] { "document_rejected", "critical_alert" };
        var languages = new[] { "en", "de", "fr" };

        foreach (var templateCode in templateCodes)
        foreach (var language in languages)
        {
            var parameters = templateCode == "document_rejected"
                ? Parameters(("documentType", "driver_card"))
                : Parameters(("ruleId", "A-05"));
            var text = NotificationTemplates.Render(templateCode, parameters, language);

            Assert.NotEmpty(text);
            Assert.NotEqual(templateCode, text);
        }
    }

    private static string Render(string templateCode, string language, params (string Key, string Value)[] values) =>
        NotificationTemplates.Render(templateCode, Parameters(values), language);

    private static IReadOnlyDictionary<string, JsonElement> Parameters(params (string Key, string Value)[] values)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(values.ToDictionary(x => x.Key, x => x.Value)));
        return document.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone());
    }
}
