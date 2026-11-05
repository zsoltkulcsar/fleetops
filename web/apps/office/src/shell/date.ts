import { formatBerlinDate } from "@fleetops/ui";

export function formatTodayBerlin(date = new Date()) {
  return formatBerlinDate(date) ?? "";
}
