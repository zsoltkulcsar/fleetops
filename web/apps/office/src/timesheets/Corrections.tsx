import { useId, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient, type components } from "@fleetops/api-client";
import { Link } from "react-router";
import { checked } from "../people/api";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";

export function Corrections({ shiftId, requestId }: { shiftId?: string; requestId?: string }) {
  const query = useQuery({
    queryKey: ["corrections"],
    queryFn: () => apiClient.GET("/api/v1/corrections").then(checked),
  });
  return (
    <section aria-label="Correction requests">
      <h3>Correction requests</h3>
      {query.isLoading && <p role="status">Loading requests...</p>}
      {query.isError && (
        <p role="alert">
          Requests could not be loaded. <button onClick={() => void query.refetch()}>Retry</button>
        </p>
      )}
      {query.data?.requests
        .filter((request) => (requestId ? request.id === requestId : request.shiftId === shiftId))
        .map((request) => (
          <CorrectionPanel key={request.id} request={request} />
        ))}
    </section>
  );
}

function CorrectionPanel({ request }: { request: components["schemas"]["CorrectionView"] }) {
  const fieldId = useId();
  const online = useOnline();
  const session = useSession();
  const client = useQueryClient();
  const [reason, setReason] = useState("");
  const [declining, setDeclining] = useState(false);
  const [shiftId, setShiftId] = useState(request.shiftId ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const allowed =
    !request.requiresHr || session.data?.roles.some((role) => role === "hr" || role === "owner");
  const decide = async (decision: "done" | "decline") => {
    if (decision === "decline" && !reason.trim()) {
      setError("Decline reason is required.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      await apiClient
        .POST("/api/v1/corrections/{id}/decide", {
          params: { path: { id: request.id } },
          body: {
            employeeId: request.employeeId,
            decision,
            reason: decision === "decline" ? reason.trim() : null,
            shiftId: shiftId || null,
          },
        })
        .then(checked);
      await Promise.all([
        client.invalidateQueries({ queryKey: ["corrections"] }),
        client.invalidateQueries({ queryKey: ["attention"] }),
        client.invalidateQueries({ queryKey: ["timesheets"] }),
      ]);
    } catch {
      setError(
        "Could not decide request. Save the shift changes first. For a forgotten day, select the added shift. Refresh and retry.",
      );
    } finally {
      setBusy(false);
    }
  };
  const shifts = useQuery({
    queryKey: ["correction-shifts", request.employeeId, request.date],
    queryFn: () =>
      apiClient.GET("/api/v1/timesheets/week", { params: { query: { week: request.date } } }).then(checked),
    enabled: !request.shiftId && request.status === "open",
  });
  return (
    <div className="ts-detail">
      <strong>
        {request.date}: {request.reason.replaceAll("_", " ")} ({request.status})
      </strong>
      {request.proposedStartAt && <p>Proposed start: {request.proposedStartAt}</p>}
      {request.proposedEndAt && <p>Proposed end: {request.proposedEndAt}</p>}
      {request.note && <p>{request.note}</p>}
      {request.requiresHr && <p>HR review required. Reopen an exported shift before editing.</p>}
      {request.declineReason && <p>Declined: {request.declineReason}</p>}
      {request.diffs.map((diff) => (
        <p key={diff.field}>
          {diff.field}: {diff.before ?? "None"} {"\u2192"} {diff.after ?? "None"}
        </p>
      ))}
      {request.status === "open" && (
        <>
          <Link
            className="ui-button"
            to={
              request.shiftId
                ? `/timesheets?shiftId=${request.shiftId}&week=${request.date}`
                : `/timesheets?addEmployeeId=${request.employeeId}&addDate=${request.date}&week=${request.date}`
            }
          >
            {request.shiftId ? "Open shift" : "Add shift"}
          </Link>
          {!request.shiftId && (
            <>
              <label htmlFor={`${fieldId}-shift`}>Added shift</label>
              <select
                id={`${fieldId}-shift`}
                value={shiftId}
                onChange={(event) => setShiftId(event.target.value)}
              >
                <option value="">Select added shift</option>
                {shifts.data?.rows
                  .filter((row) => row.employeeId === request.employeeId)
                  .flatMap((row) =>
                    row.days.filter((day) => day.date === request.date).flatMap((day) => day.shifts),
                  )
                  .map((shift) => (
                    <option key={shift.id} value={shift.id}>
                      {request.date} {shift.status} {shift.id}
                    </option>
                  ))}
              </select>
              <button className="ui-button" onClick={() => void shifts.refetch()}>
                Refresh added shifts
              </button>
            </>
          )}
          <button
            className="ui-button"
            disabled={busy || !online || !allowed || !shiftId}
            onClick={() => void decide("done")}
          >
            Mark done
          </button>
          <button
            className="ui-button"
            disabled={busy || !online || !allowed}
            onClick={() => setDeclining(true)}
          >
            Decline with reason
          </button>
          {declining && (
            <form
              onSubmit={(event) => {
                event.preventDefault();
                void decide("decline");
              }}
            >
              <label htmlFor={`${fieldId}-reason`}>Decline reason</label>
              <textarea
                id={`${fieldId}-reason`}
                required
                maxLength={1000}
                value={reason}
                onChange={(event) => setReason(event.target.value)}
              />
              <button className="ui-button" disabled={busy || !online || !allowed || !reason.trim()}>
                Decline request
              </button>
            </form>
          )}
        </>
      )}
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
