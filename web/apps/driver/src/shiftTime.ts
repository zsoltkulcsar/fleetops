export function wallTime(instant: string | number, zone: string): string {
  const parts = new Intl.DateTimeFormat("sv-SE", {
    timeZone: zone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).format(new Date(instant));
  return parts.replace(" ", "T");
}

export function companyInstant(value: string, zone: string, notBefore: string): string {
  const nominal = Date.parse(`${value}Z`);
  if (!Number.isFinite(nominal)) throw new Error("invalid_time");
  const candidates: number[] = [];
  for (let offset = -14 * 60; offset <= 14 * 60; offset += 15) {
    const candidate = nominal - offset * 60000;
    if (candidate >= Date.parse(notBefore) && candidate <= Date.now() && wallTime(candidate, zone) === value)
      candidates.push(candidate);
  }
  if (candidates.length !== 1) throw new Error(candidates.length ? "ambiguous_time" : "invalid_time");
  return new Date(candidates[0]).toISOString();
}

export function displayTime(value: string, zone: string): string {
  return new Intl.DateTimeFormat("de-DE", { timeZone: zone, hour: "2-digit", minute: "2-digit" }).format(
    new Date(value),
  );
}
