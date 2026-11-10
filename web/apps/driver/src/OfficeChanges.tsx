import type { components } from "@fleetops/api-client";
import { driverDate } from "./DriverSchedule";
import { displayTime } from "./shiftTime";
export function OfficeChanges({
  changes,
  zone,
}: {
  changes: components["schemas"]["ShiftHistory"][];
  zone: string;
}) {
  if (!changes.length) return null;
  const valueLabel = (value: string | null) => {
    if (value === null || value === "") return "None";
    if (/^\d{4}-\d{2}-\d{2}T/.test(value) && Number.isFinite(Date.parse(value))) {
      const date = new Intl.DateTimeFormat("en-CA", {
        timeZone: zone,
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
      }).format(new Date(value));
      return `${driverDate(date)} ${displayTime(value, zone)} (${zone})`;
    }
    if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return driverDate(value);
    return value.replaceAll("_", " ");
  };
  return (
    <section className="driver-callout" aria-label="Office changes">
      <h3>Changed by the office</h3>
      {changes.map((change, index) => (
        <div key={`${change.auditId}-${index}`}>
          <strong>{change.editor}</strong>
          <p>
            {valueLabel(change.at)} - {change.reason}
          </p>
          <p>
            {change.entityType} - {change.field.replace(/([a-z])([A-Z])/g, "$1 $2")}:{" "}
            {valueLabel(change.before)} {"\u2192"} {valueLabel(change.after)}
          </p>
        </div>
      ))}
    </section>
  );
}
