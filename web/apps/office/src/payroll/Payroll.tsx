import { useEffect, useState, type FormEvent } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@fleetops/api-client";
import { Banner, Drawer, Panel } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";
import { payrollApi, PayrollError, type Schema } from "./api";
import "../timesheets/timesheets.css";

export function Payroll() {
  const session = useSession();
  const allowed = session.data?.roles.some((role) => role === "hr" || role === "owner") === true;
  const [month, setMonth] = useState(() => new Date().toISOString().slice(0, 7));
  useEffect(() => {
    document.title = "Payroll export · FleetOps Office";
  }, []);
  return (
    <div className="page timesheets-page">
      <h1>Payroll export</h1>
      {allowed ? (
        <>
          <div className="ts-toolbar">
            <label htmlFor="payroll-month">Month</label>
            <input
              id="payroll-month"
              type="month"
              required
              value={month}
              onChange={(event) => {
                if (event.target.value) setMonth(event.target.value);
              }}
            />
          </div>
          <PayrollMonth key={month} month={`${month}-01`} />
        </>
      ) : (
        <Panel state="denied">
          <p>Payroll requires HR or owner access.</p>
        </Panel>
      )}
    </div>
  );
}

function PayrollMonth({ month }: { month: string }) {
  const online = useOnline();
  const session = useSession();
  const client = useQueryClient();
  const readiness = useQuery({
    queryKey: ["payroll", month, "readiness"],
    queryFn: () => payrollApi.readiness(month),
  });
  const history = useQuery({
    queryKey: ["payroll", month, "history"],
    queryFn: () => payrollApi.history(month),
  });
  const [preview, setPreview] = useState<Schema["PayrollPreview"]>();
  const [format, setFormat] = useState("csv_generic");
  const [action, setAction] = useState<{ kind: "generate" | "sent" | "discard"; id?: string }>();
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState<PayrollError>();
  const [stepUp, setStepUp] = useState(false);
  const [credential, setCredential] = useState("");
  const changed = error?.code === "export_changed";
  const refresh = async () => {
    setPreview(undefined);
    await client.invalidateQueries({ queryKey: ["payroll", month] });
    await client.invalidateQueries({ queryKey: ["timesheets"] });
  };
  const run = async (work: () => Promise<void>) => {
    setBusy(true);
    setError(undefined);
    setMessage("");
    try {
      await work();
    } catch (failure) {
      const problem = failure instanceof PayrollError ? failure : new PayrollError("request_failed");
      setError(problem);
      if (problem.code === "step_up_required") setStepUp(true);
      if (problem.code === "export_changed") setPreview(undefined);
    } finally {
      setBusy(false);
    }
  };
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    void run(async () => {
      if (!action) return;
      if (action.kind === "generate") {
        if (!preview || format !== "csv_generic") return;
        if (stepUp) {
          const verification = await apiClient.POST("/api/v1/auth/step-up", {
            body: session.data?.stepUpMethod === "password" ? { password: credential } : { code: credential },
          });
          setCredential("");
          if (!verification.response.ok) throw new PayrollError("verification_failed");
          setStepUp(false);
        }
        await payrollApi.generate({ month, format, fingerprint: preview.fingerprint });
      } else if (action.id && action.kind === "sent") {
        await payrollApi.sent(action.id, {
          sentDate: String(form.get("sentDate")),
          note: String(form.get("note") || "").trim() || null,
        });
      } else if (action.id) {
        const reason = String(form.get("reason") || "").trim();
        if (!reason) throw new PayrollError("reason_required");
        await payrollApi.discard(action.id, reason);
      }
      setMessage(
        action.kind === "generate"
          ? "Export generated."
          : action.kind === "sent"
            ? "Export marked sent."
            : "Export discarded. Shifts are approved again.",
      );
      setAction(undefined);
      await refresh();
    });
  };
  const feedback = error && (
    <Banner kind="error" label="Payroll action failed">
      <p>
        {changed
          ? "Shifts changed. Review the list and run Preview again before generating."
          : error.code === "step_up_required"
            ? "Verify your identity to generate the export."
            : error.code === "verification_failed"
              ? "Verification failed. Try again."
              : error.code === "export_already_generated"
                ? "An unsent export already exists. Mark it sent or discard it first."
                : error.code === "nothing_to_export"
                  ? "There is nothing to export for this month."
                  : "The action could not be completed. Check the fields, access and connection, then try again."}
      </p>
      {changed && (
        <ul>
          {error.shifts.map((shift) => (
            <li key={shift.shiftId}>
              Shift {shift.shiftId}, current version {shift.version}
            </li>
          ))}
        </ul>
      )}
    </Banner>
  );
  return (
    <>
      {!online && (
        <Banner kind="info" label="Offline">
          Connect to prepare or update payroll exports.
        </Banner>
      )}
      {!action && feedback}
      <p role="status">{message}</p>
      <Panel title="Month readiness">
        {readiness.isPending && <p role="status">Loading readiness...</p>}
        {readiness.isError && (
          <Banner
            kind="error"
            label="Readiness unavailable"
            retryLabel="Try again"
            onRetry={() => void readiness.refetch()}
          >
            Readiness could not be loaded.
          </Banner>
        )}
        {readiness.data && (
          <div className="ts-scroll">
            <table className="ts-grid">
              <caption>Readiness for {month.slice(0, 7)}</caption>
              <thead>
                <tr>
                  {["Person", "Approved", "Waiting", "Missing dates", "Final month", "Ready"].map((label) => (
                    <th scope="col" key={label}>
                      {label}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {readiness.data.people.map((person) => (
                  <tr key={person.employeeId}>
                    <th scope="row">
                      {person.name}
                      <small>{person.personnelNumber ?? "No personnel number"}</small>
                    </th>
                    <td>{person.approved}</td>
                    <td>{person.waiting}</td>
                    <td>{person.missing.join(", ") || "None"}</td>
                    <td>{person.finalMonth ? "Final month" : "No"}</td>
                    <td>{person.ready ? "Ready" : "Needs review"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {!readiness.data.people.length && <p>No people for this month.</p>}
          </div>
        )}
      </Panel>
      <Panel title="Prepare export">
        <div className="ts-toolbar">
          <label htmlFor="payroll-format">Format</label>
          <select
            id="payroll-format"
            value={format}
            disabled={busy}
            onChange={(event) => setFormat(event.target.value)}
          >
            <option value="csv_generic">Generic CSV</option>
            <option value="datev_lohn">DATEV Lohn</option>
          </select>
          <button
            className="ui-button"
            disabled={busy || !online || !readiness.data || readiness.isError}
            onClick={() =>
              void run(async () => {
                setPreview(undefined);
                setPreview(await payrollApi.preview(month));
              })
            }
          >
            Preview
          </button>
          <button
            className="ui-button primary"
            disabled={busy || !online || !preview || format === "datev_lohn"}
            onClick={() => {
              setError(undefined);
              setStepUp(false);
              setCredential("");
              setAction({ kind: "generate" });
            }}
          >
            Generate
          </button>
        </div>
        <p className="ts-help">
          Generic CSV layout is a design choice to confirm with payroll. Includes hours, nights, absences and
          flags; no money.
        </p>
        {format === "datev_lohn" && <p className="ts-help">DATEV layout not confirmed</p>}
        {preview && (
          <>
            <p className="ts-help">
              Preview locks nothing. {preview.fingerprint.length} shifts in this export.
            </p>
            <div className="ts-scroll">
              <table className="ts-grid">
                <caption>Preview totals per person</caption>
                <thead>
                  <tr>
                    {[
                      "Person",
                      "Driving hours",
                      "Other work hours",
                      "Availability hours",
                      "Nights",
                      "Absences",
                    ].map((label) => (
                      <th scope="col" key={label}>
                        {label}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {preview.totals.map((total) => (
                    <tr key={total.employeeId}>
                      <th scope="row">{total.name}</th>
                      <td>{total.drivingHours}</td>
                      <td>{total.otherWorkHours}</td>
                      <td>{total.availabilityHours}</td>
                      <td>{counts(total.nights)}</td>
                      <td>{counts(total.absences)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {!preview.totals.length && <p>No totals to preview.</p>}
            </div>
          </>
        )}
      </Panel>
      <Panel title="Export history">
        {history.isPending && <p role="status">Loading exports...</p>}
        {history.isError && (
          <Banner
            kind="error"
            label="History unavailable"
            retryLabel="Try again"
            onRetry={() => void history.refetch()}
          >
            Export history could not be loaded.
          </Banner>
        )}
        {history.data && (
          <div className="ts-scroll">
            <table className="ts-grid">
              <caption>Exports for {month.slice(0, 7)}</caption>
              <thead>
                <tr>
                  {["Version", "Kind", "Status", "Sent date", "Actions"].map((label) => (
                    <th scope="col" key={label}>
                      {label}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {history.data.exports.map((entry) => (
                  <tr key={entry.id}>
                    <th scope="row">{entry.version}</th>
                    <td>{entry.kind}</td>
                    <td>{entry.status}</td>
                    <td>{entry.sentDate ?? "Not sent"}</td>
                    <td>
                      {["generated", "sent"].includes(entry.status) && (
                        <button
                          className="ui-button"
                          disabled={busy || !online}
                          onClick={() =>
                            void run(async () => {
                              const link = await payrollApi.download(entry.id);
                              window.location.assign(link.url);
                            })
                          }
                        >
                          Download
                        </button>
                      )}
                      {entry.status === "generated" && (
                        <>
                          <button
                            className="ui-button"
                            disabled={busy || !online}
                            onClick={() => {
                              setError(undefined);
                              setAction({ kind: "sent", id: entry.id });
                            }}
                          >
                            Mark sent
                          </button>
                          <button
                            className="ui-button"
                            disabled={busy || !online}
                            onClick={() => {
                              setError(undefined);
                              setAction({ kind: "discard", id: entry.id });
                            }}
                          >
                            Discard
                          </button>
                        </>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {!history.data.exports.length && <p>No exports for this month.</p>}
          </div>
        )}
      </Panel>
      {action && (
        <Drawer
          open
          title={
            action.kind === "generate"
              ? "Confirm payroll export"
              : action.kind === "sent"
                ? "Mark export sent"
                : "Discard export"
          }
          closeLabel="Close confirmation"
          onClose={() => {
            if (!busy) {
              setAction(undefined);
              setCredential("");
              setError(undefined);
            }
          }}
        >
          <form className="ts-detail" onSubmit={submit}>
            {feedback}
            {action.kind === "generate" && (
              <>
                <p>
                  Confirm period {month} to{" "}
                  {new Date(Date.UTC(Number(month.slice(0, 4)), Number(month.slice(5, 7)), 0))
                    .toISOString()
                    .slice(0, 10)}{" "}
                  and {preview?.fingerprint.length ?? 0} shifts. Generating locks these shift versions.
                </p>
                {stepUp && (
                  <>
                    <label htmlFor="payroll-verification">
                      {session.data?.stepUpMethod === "password" ? "Password" : "Authentication code"}
                    </label>
                    <input
                      id="payroll-verification"
                      type={session.data?.stepUpMethod === "password" ? "password" : "text"}
                      autoComplete={
                        session.data?.stepUpMethod === "password" ? "current-password" : "one-time-code"
                      }
                      required
                      value={credential}
                      onChange={(event) => setCredential(event.target.value)}
                    />
                  </>
                )}
              </>
            )}
            {action.kind === "sent" && (
              <>
                <label htmlFor="payroll-sent-date">Sent date</label>
                <input
                  id="payroll-sent-date"
                  name="sentDate"
                  type="date"
                  required
                  max={new Date().toISOString().slice(0, 10)}
                  defaultValue={new Date().toISOString().slice(0, 10)}
                />
                <label htmlFor="payroll-note">Note (optional)</label>
                <textarea id="payroll-note" name="note" maxLength={500} />
              </>
            )}
            {action.kind === "discard" && (
              <>
                <p>Discarding an unsent export returns its shifts to approved.</p>
                <label htmlFor="payroll-discard-reason">Reason</label>
                <textarea id="payroll-discard-reason" name="reason" required maxLength={500} />
              </>
            )}
            <button
              className="ui-button primary"
              disabled={
                busy || !online || (action.kind === "generate" && (!preview || format !== "csv_generic"))
              }
            >
              {busy
                ? "Saving..."
                : action.kind === "generate"
                  ? "Confirm generate"
                  : action.kind === "sent"
                    ? "Mark sent"
                    : "Discard export"}
            </button>
            {changed && (
              <button
                type="button"
                className="ui-button"
                disabled={busy || !online}
                onClick={() => {
                  setAction(undefined);
                  void run(async () => setPreview(await payrollApi.preview(month)));
                }}
              >
                Preview again
              </button>
            )}
            <button
              type="button"
              className="ui-button"
              disabled={busy}
              onClick={() => {
                setAction(undefined);
                setCredential("");
                setError(undefined);
              }}
            >
              Cancel
            </button>
          </form>
        </Drawer>
      )}
    </>
  );
}

function counts(values: Record<string, number>) {
  return (
    Object.entries(values)
      .map(([label, count]) => `${label}: ${count}`)
      .join(", ") || "None"
  );
}
