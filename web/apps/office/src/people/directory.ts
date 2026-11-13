import type { components } from "@fleetops/api-client";

export type AbsenceItem = components["schemas"]["AbsenceDetail"];

/** Days the People directory looks ahead for planned absences. */
export const absenceWindowDays = 14;

export function berlinToday(date = new Date()) {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Berlin" }).format(date);
}

export function addDays(isoDate: string, days: number) {
  const value = new Date(`${isoDate}T12:00:00Z`);
  value.setUTCDate(value.getUTCDate() + days);
  return value.toISOString().slice(0, 10);
}

export function daysBetween(fromIso: string, toIso: string) {
  return Math.round((Date.parse(`${toIso}T12:00:00Z`) - Date.parse(`${fromIso}T12:00:00Z`)) / 86400000);
}

export function initials(name: string) {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (!parts.length) return "?";
  const first = parts[0][0] ?? "";
  const last = parts.length > 1 ? (parts[parts.length - 1][0] ?? "") : (parts[0][1] ?? "");
  return `${first}${last}`.toUpperCase();
}

/** Short day and month, e.g. 12.11. */
export function shortDate(isoDate: string) {
  const [, month, day] = isoDate.split("-");
  return `${day}.${month}.`;
}

export function absenceRange(from: string, to: string) {
  return from === to ? shortDate(from) : `${shortDate(from)}\u2013${shortDate(to)}`;
}

/** Absences that still count: approved or awaiting a decision, and not over yet. */
export function openAbsences(items: AbsenceItem[] | undefined, today: string) {
  return (items ?? [])
    .filter(
      (item) =>
        (item.absence.status === "approved" || item.absence.status === "requested") &&
        item.absence.to >= today,
    )
    .sort((a, b) => a.absence.from.localeCompare(b.absence.from));
}

export function documentPill(state: string | null | undefined): "usable" | "warning" | "missing" | "blocked" {
  if (state === "verified") return "usable";
  if (state === "expiring" || state === "uploaded") return "warning";
  if (state === "missing" || !state) return "missing";
  return "blocked";
}

export const employmentLabels: Record<string, string> = {
  full_time: "Full time",
  part_time: "Part time",
};
export const languageLabels: Record<string, string> = {
  de: "German",
  en: "English",
};

export function mondayOf(isoDate: string) {
  const day = new Date(`${isoDate}T12:00:00Z`).getUTCDay();
  return addDays(isoDate, -((day + 6) % 7));
}

/** ISO 8601 week number. */
export function weekNumber(isoDate: string) {
  const date = new Date(`${isoDate}T12:00:00Z`);
  const thursday = new Date(date);
  thursday.setUTCDate(date.getUTCDate() + 3 - ((date.getUTCDay() + 6) % 7));
  const firstThursday = new Date(Date.UTC(thursday.getUTCFullYear(), 0, 4));
  return (
    1 +
    Math.round(
      ((thursday.getTime() - firstThursday.getTime()) / 86400000 -
        3 +
        ((firstThursday.getUTCDay() + 6) % 7)) /
        7,
    )
  );
}
