import type { components } from "@fleetops/api-client";
import { useEffect, useState } from "react";

export function DriverReleaseNotice({
  release,
}: {
  release?: components["schemas"]["ReleaseBanner"] | null;
}) {
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    if (!release) return;
    const timer = window.setTimeout(
      () => setNow(Date.now()),
      Math.max(0, Date.parse(release.validUntil) - Date.now() + 1),
    );
    return () => window.clearTimeout(timer);
  }, [release]);
  if (!release || Date.parse(release.validUntil) <= now) return null;
  const date = new Intl.DateTimeFormat("de-DE", {
    day: "2-digit",
    month: "2-digit",
    timeZone: "Europe/Berlin",
  }).format(new Date(release.validUntil));
  return (
    <p className="driver-callout" role="status">
      Released by {release.releasedBy} until {date}
    </p>
  );
}
