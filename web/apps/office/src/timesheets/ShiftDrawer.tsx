import { useState, type FormEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import { Banner, Drawer, StatusPill } from "@fleetops/ui";
import { useOnline } from "../shell/Shell";
import { useSession } from "../auth/AuthGate";
import { timesheetsApi, type Schema } from "./api";
import { dateLabel, hours, instant, ShiftStatus, timeLabel } from "./presentation";
import { ShiftEditor } from "./ShiftEditor";
export function ShiftDrawer({
  id,
  zone,
  onClose,
  onSaved,
  onApprove,
}: {
  id: string;
  zone: string;
  onClose: () => void;
  onSaved: () => void;
  onApprove: (shift: Schema["GridShift"]) => void;
}) {
  const online = useOnline();
  const session = useSession();
  const query = useQuery({ queryKey: ["timesheets", "shift", id], queryFn: () => timesheetsApi.detail(id) });
  const [mode, setMode] = useState<"view" | "edit" | "reject" | "end">("view");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const detail = query.data;
  const shift = detail?.shift;
  const saved = () => {
    onSaved();
    setMode("view");
    void query.refetch();
  };
  const decide = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!shift) return;
    const form = new FormData(event.currentTarget);
    const reason = String(form.get("reason") ?? "").trim();
    if (!reason) {
      setError("Reason is required.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      if (mode === "reject") await timesheetsApi.reject(id, { version: shift.version, reason });
      else
        await timesheetsApi.end(id, {
          version: shift.version,
          reason,
          endAt: instant(String(form.get("endAt")), zone),
          odometerEndKm: form.get("odometer") ? Number(form.get("odometer")) : null,
        });
      saved();
    } catch {
      setError(
        "The shift could not be updated. Check reason and end time. Close and reopen the shift if it changed, then try again.",
      );
    } finally {
      setBusy(false);
    }
  };
  return (
    <Drawer
      open
      title={
        detail
          ? `Shift  -  ${new Intl.DateTimeFormat("en-GB", { weekday: "short", timeZone: "UTC" }).format(new Date(`${detail.shift.date}T12:00:00Z`))} ${dateLabel(detail.shift.date)}  -  ${detail.driver}  -  ${detail.truckPlate ?? "No truck"}`
          : "Shift"
      }
      closeLabel="Close shift"
      onClose={() => {
        if (!busy) onClose();
      }}
    >
      <div className="ts-detail">
        {query.isLoading && <p role="status">Loading shift...</p>}
        {query.isError && (
          <Banner kind="error" label="Error" retryLabel="Try again" onRetry={() => void query.refetch()}>
            Shift could not be loaded. Check your access and connection.
          </Banner>
        )}
        {detail && shift && (
          <>
            <div className="ts-toolbar">
              <ShiftStatus status={shift.status} />
              <span>Source: {shift.source}</span>
              <span>Company time: {zone}</span>
            </div>
            {shift.submittedAt && <p>Submitted {timeLabel(shift.submittedAt, zone)}</p>}
            <div className="ts-toolbar">
              {shift.status === "submitted" && (
                <>
                  <button
                    className="ui-button primary"
                    disabled={!online || detail.employeeId === session.data?.employeeId}
                    onClick={() =>
                      onApprove({
                        id,
                        version: shift.version,
                        status: shift.status,
                        flagged: detail.flags.length > 0,
                      })
                    }
                  >
                    Approve
                  </button>
                  <button className="ui-button" disabled={!online} onClick={() => setMode("reject")}>
                    Reject -
                  </button>
                </>
              )}
              {shift.status !== "exported" && (
                <button className="ui-button" disabled={!online} onClick={() => setMode("edit")}>
                  Edit
                </button>
              )}
              {shift.status === "running" && (
                <button className="ui-button" disabled={!online} onClick={() => setMode("end")}>
                  End shift -
                </button>
              )}
            </div>
            {detail.employeeId === session.data?.employeeId && (
              <p>Own shifts need another approver. If you are an owner, ask HR.</p>
            )}
            {shift.status === "exported" && <p>This shift is locked after export.</p>}
            {shift.rejectReason && <p>Rejected: {shift.rejectReason}. The driver can fix and resubmit.</p>}
            {mode === "edit" && (
              <ShiftEditor
                key={`${id}-${shift.version}`}
                detail={detail}
                employeeId={detail.employeeId}
                date={shift.date}
                zone={zone}
                onSaved={saved}
              />
            )}
            {(mode === "reject" || mode === "end") && (
              <form className="ts-detail" onSubmit={decide}>
                <h3>{mode === "reject" ? "Reject shift" : "End running shift"}</h3>
                {mode === "end" && (
                  <>
                    <label>
                      End - {zone}
                      <input type="datetime-local" name="endAt" required />
                    </label>
                    <label>
                      End odometer (km)
                      <input type="number" name="odometer" min={shift.odometerStartKm ?? 0} step={1} />
                    </label>
                  </>
                )}
                <label>
                  Reason
                  <textarea name="reason" required maxLength={500} />
                </label>
                {error && <p role="alert">{error}</p>}
                <button className="ui-button primary" disabled={busy || !online}>
                  {busy
                    ? "Saving..."
                    : mode === "reject"
                      ? "Reject and notify driver"
                      : "End shift and notify driver"}
                </button>
                <button type="button" className="ui-button" disabled={busy} onClick={() => setMode("view")}>
                  Cancel
                </button>
              </form>
            )}
            <h3>Activity timeline</h3>
            {shift.activities.map((activity) => (
              <div className="ts-timeline" key={activity.id}>
                <strong>{activity.type.replaceAll("_", " ")}</strong>
                <span>
                  {timeLabel(activity.startAt, zone)} ?{" "}
                  {activity.endAt ? timeLabel(activity.endAt, zone) : "Running"}
                </span>
                <span>
                  {activity.endAt
                    ? hours(Math.round((Date.parse(activity.endAt) - Date.parse(activity.startAt)) / 60000))
                    : "Running"}
                </span>
              </div>
            ))}
            <h3>Totals</h3>
            <dl className="ts-totals">
              {[
                ["Driving", shift.drivingHours],
                ["Other work", shift.otherWorkHours],
                ["Break", shift.breakHours],
                ["Availability", shift.availabilityHours],
              ].map(([label, value]) => (
                <div key={label}>
                  <dt>{label}</dt>
                  <dd>{hours(Math.round(Number(value) * 60))}</dd>
                </div>
              ))}
            </dl>
            <h3>Nights away</h3>
            {shift.overnights.length ? (
              shift.overnights.map((night) => (
                <p key={night.id}>
                  {dateLabel(night.night)} - {night.countryCode} - {night.kind}
                </p>
              ))
            ) : (
              <p>No nights away.</p>
            )}
            <p>
              Departure check:{" "}
              {shift.departureCheckOk === true
                ? "OK"
                : shift.departureCheckOk === false
                  ? "Not OK"
                  : "Not recorded"}
            </p>
            <p>
              Odometer: {shift.odometerStartKm ?? "Not recorded"} {"\u2192"}{" "}
              {shift.odometerEndKm ?? "Not recorded"} km
            </p>
            <h3>Flags</h3>
            {detail.flags.length ? (
              detail.flags.map((flag) => (
                <p key={flag}>
                  <StatusPill status="warning" />{" "}
                  {flag === "deviceTimeGap"
                    ? "Phone clock differed from server time by more than 5 min"
                    : flag.startsWith("blockedAtStart")
                      ? "Started while blocked. Check driver readiness."
                      : flag === "A-16"
                        ? "Running shift exceeded the company's maximum shift hours."
                        : flag === "A-17"
                          ? "Driver and office changes need review."
                          : flag}
                </p>
              ))
            ) : (
              <p>No flags.</p>
            )}
            <h3>Driver conflicts</h3>
            {detail.conflicts.length ? (
              detail.conflicts.map((conflict) => (
                <section key={conflict.id}>
                  <p>
                    {timeLabel(conflict.createdAt, zone)} - Driver version {conflict.baseVersion} - Office
                    version {conflict.officeVersion}
                  </p>
                  <p>Driver's proposed changes</p>
                  <pre className="ts-conflict">{conflict.driverPayload}</pre>
                </section>
              ))
            ) : (
              <p>No open conflicts.</p>
            )}
            <h3>History</h3>
            {detail.history.length ? (
              detail.history.map((entry, index) => (
                <section key={`${entry.auditId}-${index}`}>
                  <strong>
                    {entry.editor} - {timeLabel(entry.at, zone)}
                  </strong>
                  <p>
                    {entry.entityType} - {entry.field}: {formatHistory(entry.before, zone)} {"\u2192"}{" "}
                    {formatHistory(entry.after, zone)}
                  </p>
                  <p>Reason: {entry.reason}</p>
                </section>
              ))
            ) : (
              <p>No history yet.</p>
            )}
          </>
        )}
      </div>
    </Drawer>
  );
}
function formatHistory(value: string | null, zone: string) {
  if (!value) return "None";
  return /^\d{4}-\d{2}-\d{2}T/.test(value) && Number.isFinite(Date.parse(value))
    ? timeLabel(value, zone)
    : value;
}
