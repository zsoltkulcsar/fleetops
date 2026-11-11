using System.Text.Json;
using System.Text.Json.Serialization;

namespace FleetOps.Api;

public sealed record UpdateSettingsRequest
{
    [JsonPropertyName("company.payroll_cutoff_day")]
    public int? CompanyPayrollCutoffDay { get; init; }

    [JsonPropertyName("company.max_shift_hours")]
    public int? CompanyMaxShiftHours { get; init; }

    [JsonPropertyName("alerts.escalation_minutes")]
    public int? AlertsEscalationMinutes { get; init; }

    [JsonPropertyName("alerts.a03.blocked_hours")]
    public int? AlertsA03BlockedHours { get; init; }

    [JsonPropertyName("alerts.a03.queue_days")]
    public int? AlertsA03QueueDays { get; init; }

    [JsonPropertyName("alerts.a08.days")]
    public int? AlertsA08Days { get; init; }

    [JsonPropertyName("vehicles.lease_lead_days")]
    public int? VehiclesLeaseLeadDays { get; init; }

    [JsonPropertyName("documents.lead_days.licence_c_ce")]
    public int? DocumentsLeadDaysLicenceCCe { get; init; }

    [JsonPropertyName("documents.lead_days.driver_card")]
    public int? DocumentsLeadDaysDriverCard { get; init; }

    [JsonPropertyName("documents.lead_days.code95")]
    public int? DocumentsLeadDaysCode95 { get; init; }

    [JsonPropertyName("documents.lead_days.medical")]
    public int? DocumentsLeadDaysMedical { get; init; }

    [JsonPropertyName("documents.lead_days.adr_certificate")]
    public int? DocumentsLeadDaysAdrCertificate { get; init; }

    [JsonPropertyName("documents.lead_days.id_card")]
    public int? DocumentsLeadDaysIdCard { get; init; }

    [JsonPropertyName("documents.lead_days.hu_report")]
    public int? DocumentsLeadDaysHuReport { get; init; }

    [JsonPropertyName("documents.lead_days.sp_report")]
    public int? DocumentsLeadDaysSpReport { get; init; }

    [JsonPropertyName("documents.lead_days.tacho_calibration")]
    public int? DocumentsLeadDaysTachoCalibration { get; init; }

    [JsonPropertyName("documents.lead_days.insurance")]
    public int? DocumentsLeadDaysInsurance { get; init; }

    [JsonPropertyName("documents.lead_days.community_licence")]
    public int? DocumentsLeadDaysCommunityLicence { get; init; }

    [JsonPropertyName("company.releasable_doc_types")]
    public string[]? CompanyReleasableDocTypes { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalSettings { get; init; }

    public IReadOnlyDictionary<string, JsonElement> ToChanges() =>
        JsonSerializer.SerializeToElement(this).EnumerateObject()
            .Where(property => property.Value.ValueKind != JsonValueKind.Null)
            .ToDictionary(property => property.Name, property => property.Value);
}
