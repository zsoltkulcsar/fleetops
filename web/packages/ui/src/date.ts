export function formatBerlinDate(input: string | Date, short = false): string | null {
  try {
    const date = input instanceof Date ? input : new Date(input);
    if (Number.isNaN(date.getTime())) return null;
    return new Intl.DateTimeFormat("en-GB", {
      timeZone: "Europe/Berlin",
      day: "2-digit",
      month: "2-digit",
      ...(short ? {} : { year: "numeric" }),
    })
      .format(date)
      .replace(/\//g, ".");
  } catch {
    return null;
  }
}
