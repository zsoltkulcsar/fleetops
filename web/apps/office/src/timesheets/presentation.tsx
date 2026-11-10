import { StatusPill, type StatusName } from "@fleetops/ui";
export const hours = (minutes: number) =>
  `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, "0")}`;
export const dateLabel = (value: string) => value.slice(0, 10).split("-").reverse().join(".");
export function addDays(value: string, days: number) {
  const date = new Date(`${value}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}
export function monday(zone: string) {
  const today = new Intl.DateTimeFormat("en-CA", {
    timeZone: zone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
  return addDays(today, -((new Date(`${today}T12:00:00Z`).getUTCDay() + 6) % 7));
}
export function ShiftStatus({ status }: { status: string }) {
  const known: Record<string, Exclude<StatusName, "absence" | "expired" | "expiring">> = {
    awaiting: "submitted",
    submitted: "submitted",
    approved: "approved",
    exported: "exported",
    draft: "draft",
    running: "running",
    rejected: "shift-rejected",
    missing: "missing-day",
    empty: "no-driver",
  };
  return status === "absence" ? (
    <StatusPill status="absence" label="Absence" />
  ) : (
    <StatusPill status={known[status] ?? "info"} label={status === "empty" ? "No shift" : undefined} />
  );
}
export function wallTime(instant: string, zone: string) {
  return new Intl.DateTimeFormat("sv-SE", {
    timeZone: zone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  })
    .format(new Date(instant))
    .replace(" ", "T");
}
export function instant(value: string, zone: string) {
  const nominal = Date.parse(`${value}Z`);
  const candidates: number[] = [];
  for (let offset = -840; offset <= 840; offset += 15) {
    const candidate = nominal - offset * 60000;
    if (Number.isFinite(candidate) && wallTime(new Date(candidate).toISOString(), zone) === value)
      candidates.push(candidate);
  }
  if (candidates.length !== 1) throw new Error("Choose valid, unambiguous times in company time.");
  return new Date(candidates[0]).toISOString();
}
export const timeLabel = (value: string, zone: string) =>
  `${dateLabel(wallTime(value, zone))} ${wallTime(value, zone).slice(11)}`;
