import { useState, type FormEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@fleetops/api-client";
import type { components } from "@fleetops/api-client";
import { Drawer, StatusPill } from "@fleetops/ui";
import { commandQueue, newCommand } from "./storage/commandQueue";
import type { DriverSnapshot } from "./storage/snapshot";
import { driverDate, syncTime } from "./DriverSchedule";
import { usePlanningQueue } from "./usePlanningQueue";
import { absenceLabels, absenceStatusLabels } from "./planningLabels";

export function DriverAbsences({ snapshot, online }: { snapshot?: DriverSnapshot; online: boolean }) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const queue = usePlanningQueue();
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [halfDay, setHalfDay] = useState<components["schemas"]["HalfDay"]>(null);
  const waiting = queue.commands.filter((command) => command.type === "absence.request");
  const preview = useQuery({
    queryKey: ["absence-preview", from, to, halfDay],
    enabled: online && open && Boolean(from && to && from <= to),
    queryFn: async () => {
      const result = await apiClient.GET("/api/v1/absences/preview", {
        params: { query: { from, to, halfDay: from === to ? halfDay : null, type: "vacation" } },
      });
      if (!result.response.ok || !result.data) throw new Error("preview_failed");
      return result.data;
    },
  });
  const holidayCoverageStart = snapshot
    ? new Intl.DateTimeFormat("en-CA", {
        timeZone: "Europe/Berlin",
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
      }).format(new Date(snapshot.generatedAt))
    : undefined;
  const coverageEnd = holidayCoverageStart ? new Date(holidayCoverageStart + "T12:00:00Z") : undefined;
  if (coverageEnd) {
    const day = coverageEnd.getUTCDate();
    coverageEnd.setUTCDate(1);
    coverageEnd.setUTCFullYear(coverageEnd.getUTCFullYear() + 1);
    const lastDay = new Date(
      Date.UTC(coverageEnd.getUTCFullYear(), coverageEnd.getUTCMonth() + 1, 0),
    ).getUTCDate();
    coverageEnd.setUTCDate(Math.min(day, lastDay));
  }
  const covered = Boolean(
    snapshot?.holidays &&
      holidayCoverageStart &&
      coverageEnd &&
      from >= holidayCoverageStart &&
      to < coverageEnd.toISOString().slice(0, 10),
  );
  const weekdays = (() => {
    if (!from || !to || from > to) return 0;
    const first = Date.parse(`${from}T12:00:00Z`);
    const last = Date.parse(`${to}T12:00:00Z`);
    if ((last - first) / 86_400_000 > 3660) return null;
    let days = 0;
    for (let time = first; time <= last; time += 86_400_000) {
      const day = new Date(time).getUTCDay();
      const date = new Date(time).toISOString().slice(0, 10);
      if (day !== 0 && day !== 6 && !snapshot?.holidays?.some((holiday) => holiday.date === date))
        days += from === to && halfDay ? 0.5 : 1;
    }
    return days;
  })();
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!snapshot || !from || to < from) return;
    setBusy(true);
    setError("");
    const form = new FormData(event.currentTarget);
    try {
      await commandQueue.enqueue(
        newCommand("absence.request", crypto.randomUUID(), 0, {
          from,
          to,
          halfDay: from === to ? halfDay : null,
          note: String(form.get("note") ?? "").trim() || null,
        }),
      );
      setOpen(false);
    } catch {
      setError("Your request could not be saved. Retry before closing this form.");
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="driver-screen" aria-label="Vacation">
      <h2>Vacation</h2>
      <p>
        {snapshot?.vacationBalance
          ? `${snapshot.vacationBalance.remaining} days left`
          : "Days left unavailable. Connect and refresh."}
      </p>
      {!online && snapshot && <p className="driver-sync">As of {syncTime(snapshot.generatedAt)}</p>}
      <button disabled={!snapshot} onClick={() => setOpen(true)}>
        Request vacation
      </button>
      {(error || queue.error) && (
        <p role="alert" className="driver-error">
          {error || queue.error}
        </p>
      )}
      {waiting.map((command) => (
        <p key={command.commandId} role="status">
          Vacation request{" "}
          {command.state === "waiting" ? "Waiting to sync" : "Could not sync. Contact the office."}
        </p>
      ))}
      {!snapshot?.absences?.length && !waiting.length && <p>No requests yet.</p>}
      {snapshot?.absences?.map((absence) => (
        <article className="driver-card" key={absence.id}>
          <h3>{absenceLabels[absence.type]}</h3>
          <p>
            {driverDate(absence.from)} to {driverDate(absence.to)}
            {absence.halfDay ? ` \u00b7 ${absence.halfDay === "am" ? "Morning" : "Afternoon"}` : ""}
          </p>
          <p>
            <StatusPill
              status={
                absence.status === "approved"
                  ? "approved"
                  : absence.status === "declined"
                    ? "rejected"
                    : absence.status === "requested"
                      ? "submitted"
                      : "absent"
              }
              label={absenceStatusLabels[absence.status]}
            />
          </p>
          {absence.declineReason && <p>Decline reason: {absence.declineReason}</p>}
          {absence.status === "requested" && (
            <button
              disabled={
                busy ||
                queue.commands.some(
                  (command) => command.type === "absence.cancel" && command.aggregateId === absence.id,
                )
              }
              onClick={async () => {
                setBusy(true);
                try {
                  await commandQueue.enqueue(newCommand("absence.cancel", absence.id, 0, {}));
                } catch {
                  setError("Cancellation could not be saved. Try again.");
                } finally {
                  setBusy(false);
                }
              }}
            >
              Cancel request
            </button>
          )}
          {queue.commands.some(
            (command) =>
              command.type === "absence.cancel" &&
              command.aggregateId === absence.id &&
              command.state === "waiting",
          ) && <p role="status">Cancellation: Waiting to sync</p>}
        </article>
      ))}
      {open && (
        <Drawer
          open
          title="Request vacation"
          closeLabel="Close vacation request"
          onClose={() => {
            if (!busy) setOpen(false);
          }}
        >
          <form className="driver-sheet" onSubmit={submit}>
            <label>
              From
              <input
                type="date"
                required
                value={from}
                onChange={(event) => {
                  setFrom(event.target.value);
                  setHalfDay(null);
                }}
              />
            </label>
            <label>
              To
              <input
                type="date"
                required
                min={from}
                value={to}
                onChange={(event) => {
                  setTo(event.target.value);
                  setHalfDay(null);
                }}
              />
            </label>
            <label>
              Half day
              <select
                disabled={!from || from !== to}
                value={halfDay ?? ""}
                onChange={(event) =>
                  setHalfDay(event.target.value === "am" ? "am" : event.target.value === "pm" ? "pm" : null)
                }
              >
                <option value="">Full day</option>
                <option value="am">Morning</option>
                <option value="pm">Afternoon</option>
              </select>
            </label>
            <label>
              Note (optional)
              <textarea name="note" maxLength={2000} />
            </label>
            <p>
              {online
                ? preview.isError
                  ? "Preview could not be loaded."
                  : preview.data
                    ? `${preview.data.countedDays} working days`
                    : "Loading preview\u2026"
                : !covered
                  ? "Connect to preview dates outside the cached holiday calendar."
                  : weekdays === null
                    ? "Choose up to ten years."
                    : `${weekdays} working days (cached holidays)`}
            </p>
            {online && preview.data && (
              <>
                <p>
                  Balance before {preview.data.balanceBefore} days; after {preview.data.balanceAfter} days
                </p>
                {preview.data.overBalance && <p role="status">Over vacation balance</p>}
                <p>Others absent on these dates: {preview.data.othersAbsentCount}</p>
              </>
            )}
            <p>{"Day counting: Mon\u2013Fri without holidays (to confirm with payroll)"}</p>
            {error && <p role="alert">{error}</p>}
            <button className="primary" disabled={busy || weekdays === null}>
              {busy ? "Saving\u2026" : "Send request"}
            </button>
          </form>
        </Drawer>
      )}
    </section>
  );
}
