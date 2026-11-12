export const dateValue = (date: Date) => date.toISOString().slice(0, 10);
export function addDays(value: string, days: number) {
  const date = new Date(`${value}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return dateValue(date);
}
export function dateLabel(value: string) {
  return new Intl.DateTimeFormat("de-DE", { day: "2-digit", month: "2-digit", timeZone: "UTC" }).format(
    new Date(`${value}T12:00:00Z`),
  );
}
export function fullDateLabel(value: string) {
  return new Intl.DateTimeFormat("de-DE", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    timeZone: "UTC",
  }).format(new Date(`${value}T12:00:00Z`));
}
export function today() {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: "Europe/Berlin",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
}
export function monday() {
  const current = today();
  const date = new Date(`${current}T12:00:00Z`);
  return addDays(current, -((date.getUTCDay() + 6) % 7));
}
export const dayDifference = (from: string, to: string) =>
  Math.round((Date.parse(`${to}T12:00:00Z`) - Date.parse(`${from}T12:00:00Z`)) / 86_400_000);

export function validDate(value: string | null): value is string {
  if (!value || value.length !== 10 || value < "1900-01-01" || value > "9998-12-01") return false;
  const parsed = new Date(`${value}T12:00:00Z`);
  return Number.isFinite(parsed.getTime()) && dateValue(parsed) === value;
}
// ISO 8601 week number: the week that holds the year's first Thursday is week 1.
export function isoWeek(value: string) {
  const date = new Date(`${value}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() + 3 - ((date.getUTCDay() + 6) % 7));
  const firstThursday = new Date(Date.UTC(date.getUTCFullYear(), 0, 4, 12));
  firstThursday.setUTCDate(firstThursday.getUTCDate() + 3 - ((firstThursday.getUTCDay() + 6) % 7));
  return 1 + Math.round((date.getTime() - firstThursday.getTime()) / (7 * 86_400_000));
}
