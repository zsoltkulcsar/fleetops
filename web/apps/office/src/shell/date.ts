import { formatBerlinDate } from "@fleetops/ui";

export function formatTodayBerlin(date = new Date()) {
  return formatBerlinDate(date) ?? "";
}

export function formatDocumentDate(value: string | null) {
  return value ? (formatBerlinDate(value) ?? "—") : "—";
}

export function formatBerlinDateTime(value: string | null) {
  if (!value) return "—";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";
  const time = new Intl.DateTimeFormat("en-GB", {
    timeZone: "Europe/Berlin",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).format(date);
  return `${formatBerlinDate(date) ?? "—"} ${time}`;
}
