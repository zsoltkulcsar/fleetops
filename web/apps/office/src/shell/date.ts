import { formatBerlinDate } from "@fleetops/ui";

export function formatTodayBerlin(date = new Date()) {
  return formatBerlinDate(date) ?? "";
}

export function formatDocumentDate(value: string | null) {
  return value ? (formatBerlinDate(value) ?? "—") : "—";
}
