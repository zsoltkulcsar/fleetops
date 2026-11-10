import type { components } from "@fleetops/api-client";
export function OfficeChanges({
  changes,
  zone,
}: {
  changes: components["schemas"]["ShiftHistory"][];
  zone: string;
}) {
  if (!changes.length) return null;
  return (
    <section className="driver-callout" aria-label="Office changes">
      <h3>Changed by the office</h3>
      {changes.map((change, index) => (
        <div key={`${change.auditId}-${index}`}>
          <strong>{change.editor}</strong>
          <p>
            {formatHistory(change.at, zone)} {"\u00B7"} {change.reason}
          </p>
          <p>
            {historyLabel(change.entityType, change.field)}: {formatHistory(change.before, zone)} {"\u2192"}{" "}
            {formatHistory(change.after, zone)}
          </p>
        </div>
      ))}
    </section>
  );
}

function wordLabel(value: string) {
  const labels: Record<string, string> = {
    other_work: "Other work",
    driving: "Driving",
    availability: "Availability",
    break: "Break",
    cab: "Cab",
    hotel: "Hotel",
    other: "Other",
    submitted: "Awaiting approval",
    running: "Running",
    draft: "Draft",
    rejected: "Rejected",
    approved: "Approved",
    exported: "Exported",
  };
  return labels[value] ?? value;
}
function historyLabel(entity: string, field: string) {
  const activity = entity.toLowerCase().includes("activity");
  const labels: Record<string, string> = {
    startAt: activity ? "Activity start" : "Shift start",
    endAt: activity ? "Activity end" : "Shift end",
    truckId: "Truck",
    trailerId: "Trailer",
    odometerStartKm: "Odometer start",
    odometerEndKm: "Odometer end",
    type: "Activity type",
    night: "Night",
    countryCode: "Night country",
    kind: "Night kind",
    status: "Status",
  };
  return labels[field] ?? "Shift";
}
function formatHistory(value: string | null, zone: string) {
  if (!value) return "None";
  if (/^\d{4}-\d{2}-\d{2}T/.test(value) && Number.isFinite(Date.parse(value))) {
    const parts = new Intl.DateTimeFormat("en-GB", {
      timeZone: zone,
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      hourCycle: "h23",
    }).formatToParts(new Date(value));
    const part = (type: Intl.DateTimeFormatPartTypes) => parts.find((item) => item.type === type)!.value;
    return `${part("day")}.${part("month")}.${part("year")} ${part("hour")}:${part("minute")}`;
  }
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return value.split("-").reverse().join(".");
  return wordLabel(value);
}
