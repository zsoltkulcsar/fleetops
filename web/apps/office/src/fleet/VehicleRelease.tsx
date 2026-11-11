import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient, type components } from "@fleetops/api-client";
import { Banner, Drawer } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { checked, PeopleError } from "../people/api";
import { documentLabel } from "../people/labels";
import { fleetApi } from "./api";

export function ReleaseNotice({
  release,
}: {
  release?: components["schemas"]["ActiveVehicleRelease"] | null;
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
    timeZone: "Europe/Berlin",
    day: "2-digit",
    month: "2-digit",
  }).format(new Date(release.validUntil));
  return (
    <Banner label="Released">
      Released by {release.byName} until {date}
    </Banner>
  );
}

function endOfDay(tomorrow: boolean) {
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone: "Europe/Berlin",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(new Date());
  const part = (type: Intl.DateTimeFormatPartTypes) =>
    Number(parts.find((value) => value.type === type)?.value);
  const midnight = Date.UTC(part("year"), part("month") - 1, part("day") + (tomorrow ? 2 : 1));
  // Resolve the Berlin offset at the next midnight, including DST boundaries.
  const hour = Number(
    new Intl.DateTimeFormat("en-GB", { timeZone: "Europe/Berlin", hour: "2-digit", hourCycle: "h23" }).format(
      new Date(midnight),
    ),
  );
  return new Date(midnight - hour * 3_600_000 - 1).toISOString();
}

export function VehicleRelease({ id }: { id: string }) {
  const session = useSession();
  const client = useQueryClient();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const allowed = session.data?.roles.some((role) => role === "dispatcher" || role === "owner");
  const detail = useQuery({ queryKey: ["fleet", "vehicle", id], queryFn: () => fleetApi.detail(id) });
  const data = detail.data;
  const eligible = data?.release?.allowed === true;
  const blockedTypes = [
    ...new Set([
      ...(data?.release?.expiredTypes.filter((type) => !data.release?.releasableTypes.includes(type)) ?? []),
      ...(data?.causes
        .filter((cause) => cause.code === "document_missing")
        .flatMap((cause) => (cause.documentType ? [cause.documentType] : [])) ?? []),
    ]),
  ];
  const blockedCondition = data?.causes.some((cause) => cause.code === "critical_defect")
    ? "critical defect"
    : "vehicle condition";
  return (
    <div onClick={(event) => event.stopPropagation()} onKeyDown={(event) => event.stopPropagation()}>
      <ReleaseNotice release={data?.release?.activeOverride} />
      {allowed && data?.release?.enabled && !eligible && (
        <p>
          Release blocked by:{" "}
          {blockedTypes.length ? blockedTypes.map(documentLabel).join(", ") : blockedCondition}.
        </p>
      )}
      {allowed && eligible && (
        <button className="ui-button" disabled={!navigator.onLine} onClick={() => setOpen(true)}>
          Release
        </button>
      )}
      <Drawer open={open} title="Release vehicle" closeLabel="Cancel release" onClose={() => setOpen(false)}>
        <form
          onSubmit={async (event) => {
            event.preventDefault();
            const form = new FormData(event.currentTarget);
            setBusy(true);
            setError("");
            try {
              await apiClient
                .POST("/api/v1/fleet/vehicles/{id}/release", {
                  params: { path: { id } },
                  body: {
                    reason: String(form.get("reason")),
                    validUntil: endOfDay(form.get("until") === "tomorrow"),
                  },
                })
                .then(checked);
              await client.invalidateQueries({ queryKey: ["fleet"] });
              await client.invalidateQueries({ queryKey: ["readiness-summary"] });
              setOpen(false);
            } catch (failure) {
              setError(
                failure instanceof PeopleError && failure.code === "release_not_enabled"
                  ? "Release is not enabled for these documents. Contact the owner."
                  : "This vehicle cannot be released. Check its documents and current condition.",
              );
            } finally {
              setBusy(false);
            }
          }}
        >
          <p>A temporary release does not renew expired documents. The owner will be asked to review it.</p>
          <label htmlFor={`release-reason-${id}`}>Reason</label>
          <textarea id={`release-reason-${id}`} name="reason" required maxLength={500} />
          <label htmlFor={`release-until-${id}`}>Release until</label>
          <select id={`release-until-${id}`} name="until">
            <option value="today">End of today</option>
            <option value="tomorrow">End of tomorrow</option>
          </select>
          {error && <p role="alert">{error}</p>}
          <button className="ui-button primary" disabled={busy || !navigator.onLine}>
            Confirm release
          </button>
        </form>
      </Drawer>
    </div>
  );
}
