import { useState, type FormEvent } from "react";
import { Drawer, StatusPill } from "@fleetops/ui";
import type { DriverShiftState } from "./useShifts";
import { companyDate, shiftHours, type ShiftView, type ActivityType } from "./storage/shiftProjection";
import { companyInstant, displayTime, wallTime } from "./shiftTime";
import { driverDate } from "./DriverSchedule";
import { OfficeChanges } from "./OfficeChanges";

function weekStart(date: string): string {
  const value = new Date(`${date}T12:00:00Z`);
  value.setUTCDate(value.getUTCDate() - ((value.getUTCDay() + 6) % 7));
  return value.toISOString().slice(0, 10);
}
function addDays(date: string, days: number): string {
  const value = new Date(`${date}T12:00:00Z`);
  value.setUTCDate(value.getUTCDate() + days);
  return value.toISOString().slice(0, 10);
}

export function DriverWeek({ state }: { state: DriverShiftState }) {
  const zone = state.snapshot?.companyTimeZone ?? "Europe/Berlin";
  const [from, setFrom] = useState(() => weekStart(companyDate(new Date().toISOString(), zone)));
  const [opened, setOpened] = useState<string>();
  const [edit, setEdit] = useState<string>();
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const to = addDays(from, 7);
  const shifts = state.shifts.filter((shift) => shift.date >= from && shift.date < to);
  const drafts = shifts.filter((shift) => shift.status === "draft" || shift.status === "rejected");
  const detail = state.shifts.find((shift) => shift.id === opened);
  const editing = detail?.activities.find((activity) => activity.id === edit);
  const nights = new Map<string, number>();
  shifts
    .flatMap((shift) => shift.overnights)
    .forEach((night) => nights.set(night.countryCode, (nights.get(night.countryCode) ?? 0) + 1));
  const pending = (shift: ShiftView) => state.commands.some((command) => command.aggregateId === shift.id);
  const submit = async () => {
    setSubmitting(true);
    try {
      for (const shift of drafts)
        if (!(await state.record("shift.submit", shift.id, shift.version, {}))) break;
    } finally {
      setSubmitting(false);
    }
  };
  const saveEdit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!detail || !editing) return;
    setError("");
    const form = new FormData(event.currentTarget);
    try {
      const startValue = String(form.get("startAt"));
      const endValue = String(form.get("endAt"));
      const startAt =
        startValue === wallTime(editing.startAt, zone)
          ? editing.startAt
          : companyInstant(startValue, zone, detail.startAt);
      const endAt = editing.endAt
        ? endValue === wallTime(editing.endAt, zone)
          ? editing.endAt
          : companyInstant(endValue, zone, startAt)
        : null;
      if (endAt && Date.parse(endAt) < Date.parse(startAt)) throw new Error("invalid_time");
      const index = detail.activities.findIndex((activity) => activity.id === editing.id);
      if (
        (index === 0 && Date.parse(startAt) !== Date.parse(detail.startAt)) ||
        (index === detail.activities.length - 1 &&
          endAt !== detail.endAt &&
          Date.parse(endAt ?? "") !== Date.parse(detail.endAt ?? "")) ||
        (index > 0 && Date.parse(startAt) < Date.parse(detail.activities[index - 1].startAt)) ||
        (index < detail.activities.length - 1 &&
          endAt &&
          detail.activities[index + 1].endAt &&
          Date.parse(endAt) > Date.parse(detail.activities[index + 1].endAt!))
      )
        throw new Error("invalid_time");
      if (
        await state.record("shift.edit-activity", detail.id, detail.version, {
          activityId: editing.id,
          type: form.get("type") as ActivityType,
          startAt,
          endAt,
        })
      )
        setEdit(undefined);
    } catch {
      setError(
        "Choose contiguous activity times inside the shift, in company time. Keep the shift’s first start and last end unchanged.",
      );
    }
  };
  return (
    <section className="driver-screen driver-data-screen">
      <h1>Week</h1>
      <h2>Hours and nights</h2>
      <div className="driver-week-nav">
        <button aria-label="Previous week" onClick={() => setFrom(addDays(from, -7))}>
          ←
        </button>
        <span>
          {driverDate(from)} – {driverDate(addDays(to, -1))}
        </span>
        <button aria-label="Next week" onClick={() => setFrom(addDays(from, 7))}>
          →
        </button>
      </div>
      <p className="driver-sync">Times shown in {zone}</p>
      {state.error && (
        <div className="driver-error" role="alert">
          <p>{state.error}</p>
          <button onClick={() => void state.sync()}>Retry</button>
        </div>
      )}
      {state.loading ? (
        <p role="status">Loading hours…</p>
      ) : (
        <>
          <div className="driver-shift-hours">
            {shifts.reduce((total, shift) => total + shiftHours(shift), 0).toFixed(2)}
            <span>hours excluding breaks</span>
          </div>
          <p>
            {[...nights]
              .map(([country, count]) => `${country}: ${count} night${count === 1 ? "" : "s"}`)
              .join(" · ") || "No nights away recorded"}
          </p>
          {shifts.length ? (
            <ul className="driver-shift-list">
              {shifts.map((shift) => (
                <li key={shift.id}>
                  <button
                    onClick={() => {
                      setOpened(shift.id);
                      setEdit(undefined);
                    }}
                  >
                    <strong>
                      {driverDate(shift.date)} · {shiftHours(shift).toFixed(2)} h
                    </strong>
                    <span>
                      <StatusPill status={shift.status === "rejected" ? "shift-rejected" : shift.status} />
                      {pending(shift)
                        ? state.commands.some(
                            (command) => command.aggregateId === shift.id && command.state !== "waiting",
                          )
                          ? " · Needs review"
                          : " · Waiting to sync"
                        : ""}
                    </span>
                    {(shift.officeChanges?.length ?? 0) > 0 && (
                      <span>Changed by the office. Open for differences.</span>
                    )}
                    {shift.rejectReason && <span>Rejected: {shift.rejectReason} · Fix and resubmit</span>}
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <p>No shifts recorded in this week.</p>
          )}
          <button
            className="primary"
            disabled={submitting || state.saving || drafts.length === 0}
            onClick={() => void submit()}
          >
            {submitting ? "Saving…" : `Submit week (${drafts.length} shift${drafts.length === 1 ? "" : "s"})`}
          </button>
        </>
      )}
      {detail && (
        <Drawer
          open
          title={driverDate(detail.date)}
          closeLabel="Close shift"
          onClose={() => {
            if (!state.saving) setOpened(undefined);
          }}
        >
          <div className="driver-sheet">
            <p>
              <StatusPill status={detail.status === "rejected" ? "shift-rejected" : detail.status} /> ·{" "}
              {shiftHours(detail).toFixed(2)} hours · {zone}
            </p>
            {detail.rejectReason && <p className="driver-error">{detail.rejectReason}. Fix and resubmit.</p>}
            {(detail.blockedAtStart || detail.deviceTimeGap) && (
              <p className="driver-callout">This shift is flagged for Office review.</p>
            )}
            {(state.snapshot?.conflicts ?? [])
              .filter((conflict) => conflict.shiftId === detail.id)
              .map((conflict) => (
                <div className="driver-callout" key={conflict.commandId}>
                  <strong>{conflict.changedBy} changed this shift.</strong>
                  <p>The Office version has been kept. Your saved change is available for review.</p>
                  {conflict.changedFields.length > 0 && (
                    <p>
                      Changed:{" "}
                      {conflict.changedFields
                        .map((field) => field.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase())
                        .join(", ")}
                      .
                    </p>
                  )}
                  {conflict.proposedStartAt && (
                    <p>Your proposed start: {displayTime(conflict.proposedStartAt, zone)}</p>
                  )}
                  {conflict.proposedEndAt && (
                    <p>Your proposed end: {displayTime(conflict.proposedEndAt, zone)}</p>
                  )}
                </div>
              ))}
            <OfficeChanges changes={detail.officeChanges ?? []} zone={zone} />
            {detail.activities.map((activity) => (
              <div className="driver-card" key={activity.id}>
                <strong>{activity.type.replaceAll("_", " ")}</strong>
                <p>
                  {displayTime(activity.startAt, zone)} –{" "}
                  {activity.endAt ? displayTime(activity.endAt, zone) : "Running"}
                </p>
                {["running", "draft", "rejected"].includes(detail.status) && (
                  <button
                    disabled={state.saving}
                    onClick={() => {
                      setEdit(activity.id);
                      setError("");
                    }}
                  >
                    Edit activity
                  </button>
                )}
              </div>
            ))}
            {detail.overnights.map((night) => (
              <p key={night.id}>
                {driverDate(night.night)} · {night.countryCode} · {night.kind}
              </p>
            ))}
            {editing && (
              <form onSubmit={saveEdit} className="driver-sheet">
                <h3>Edit activity</h3>
                <label>
                  Activity
                  <select name="type" defaultValue={editing.type}>
                    {(["driving", "other_work", "availability", "break"] as const).map((type) => (
                      <option key={type} value={type} disabled={type === "driving" && !detail.truckId}>
                        {type.replaceAll("_", " ")}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Start · {zone}
                  <input
                    type="datetime-local"
                    name="startAt"
                    defaultValue={wallTime(editing.startAt, zone)}
                    required
                  />
                </label>
                {editing.endAt && (
                  <label>
                    End · {zone}
                    <input
                      type="datetime-local"
                      name="endAt"
                      defaultValue={wallTime(editing.endAt, zone)}
                      required
                    />
                  </label>
                )}
                {error && (
                  <p className="driver-error" role="alert">
                    {error}
                  </p>
                )}
                <button disabled={state.saving}>Save activity</button>
              </form>
            )}
            {detail.status === "rejected" && (
              <button
                disabled={state.saving}
                onClick={() => void state.record("shift.submit", detail.id, detail.version, {})}
              >
                Resubmit shift
              </button>
            )}
          </div>
        </Drawer>
      )}
    </section>
  );
}
