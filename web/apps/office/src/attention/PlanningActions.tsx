import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@fleetops/api-client";
import { Banner, Drawer } from "@fleetops/ui";
import { Link } from "react-router";
import { checked } from "../people/api";
import { timesheetsApi } from "../timesheets/api";
import { absencesApi, absenceLabels } from "../absences/api";
import { formatDocumentDate } from "../shell/date";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";
import type { AttentionItem, AttentionGroup } from "./api";

export function PlanningActions({ item, group }: { item: AttentionItem; group: AttentionGroup }) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const client = useQueryClient();
  const online = useOnline();
  const session = useSession();
  const hr = session.data?.roles.includes("hr") === true;
  const dispatch = session.data?.roles.some((role) => role === "dispatcher" || role === "owner") === true;
  const requests = useQuery({
    queryKey: ["schedule-requests"],
    queryFn: () => apiClient.GET("/api/v1/schedule/requests").then(checked),
    enabled: open && item.ruleId === "A-19",
  });
  const shift = useQuery({
    queryKey: ["timesheets", "shift", group.subjectId],
    queryFn: () => timesheetsApi.detail(group.subjectId),
    enabled: open && item.ruleId === "A-18",
  });
  const absences = useQuery({
    queryKey: ["absences", "worked", group.subjectId],
    queryFn: () => absencesApi.list(shift.data!.shift.date, shift.data!.shift.date, shift.data!.employeeId),
    enabled: open && item.ruleId === "A-18" && Boolean(shift.data),
  });
  const request = requests.data?.items.find((entry) => entry.id === group.subjectId);
  const decide = async (accept: boolean) => {
    if (!dispatch || !online || busy || (!accept && !reason.trim())) return;
    setBusy(true);
    setError("");
    try {
      await apiClient
        .POST("/api/v1/schedule/requests/{id}/decide", {
          params: { path: { id: group.subjectId } },
          body: { accept, reason: accept ? null : reason.trim() },
        })
        .then(checked);
      await client.invalidateQueries({ queryKey: ["attention"] });
      await client.invalidateQueries({ queryKey: ["schedule"] });
      await client.invalidateQueries({ queryKey: ["schedule-requests"] });
      setOpen(false);
    } catch {
      setError("The request could not be decided. Check for schedule clashes, refresh and try again.");
    } finally {
      setBusy(false);
    }
  };
  return (
    <>
      <button className="ui-button primary" onClick={() => setOpen(true)}>
        {item.ruleId === "A-18" ? "Review absence" : "Review change request"}
      </button>
      {open && (
        <Drawer
          open
          title={item.ruleId === "A-18" ? "Worked during absence" : "Schedule change request"}
          closeLabel="Close request review"
          onClose={() => {
            if (!busy) setOpen(false);
          }}
        >
          <div className="people-form">
            {item.ruleId === "A-18" ? (
              <>
                {shift.isLoading || absences.isLoading ? (
                  <p role="status">{"Loading absence\u2026"}</p>
                ) : null}
                {(shift.isError || absences.isError) && (
                  <Banner
                    kind="error"
                    label="Absence unavailable"
                    retryLabel="Try again"
                    onRetry={() => {
                      void shift.refetch();
                      void absences.refetch();
                    }}
                  >
                    Check the connection and try again.
                  </Banner>
                )}
                {absences.data?.items
                  .filter((entry) => entry.absence.status === "approved")
                  .map((entry) => (
                    <p key={entry.absence.id}>
                      {absenceLabels[entry.absence.type]}: {formatDocumentDate(entry.absence.from)} to{" "}
                      {formatDocumentDate(entry.absence.to)}{" "}
                      {hr && (
                        <Link className="ui-button" to={`/people/absences?absenceId=${entry.absence.id}`}>
                          Change absence
                        </Link>
                      )}
                    </p>
                  ))}
                {hr && (
                  <button
                    className="ui-button primary"
                    disabled={!online || busy}
                    onClick={async () => {
                      setBusy(true);
                      setError("");
                      try {
                        await apiClient
                          .POST("/api/v1/alerts/{id}/acknowledge-absence", {
                            params: { path: { id: item.id } },
                            body: { version: item.version },
                          })
                          .then(checked);
                        await client.invalidateQueries({ queryKey: ["attention"] });
                        setOpen(false);
                      } catch {
                        setError("Acknowledgement failed. Refresh the alert and try again.");
                      } finally {
                        setBusy(false);
                      }
                    }}
                  >
                    Acknowledge
                  </button>
                )}
              </>
            ) : (
              <>
                {requests.isLoading && <p role="status">{"Loading request\u2026"}</p>}
                {requests.isError && (
                  <Banner
                    kind="error"
                    label="Request unavailable"
                    retryLabel="Try again"
                    onRetry={() => void requests.refetch()}
                  >
                    Check the connection and try again.
                  </Banner>
                )}
                {request ? (
                  <>
                    <p>
                      Requested dates: {formatDocumentDate(request.proposedFrom)} to{" "}
                      {formatDocumentDate(request.proposedTo)}
                    </p>
                    <p>{request.note}</p>
                    <p>Accepting changes this Cycle only. The pattern stays the same.</p>
                    {request.status === "open" && dispatch && (
                      <>
                        <label>
                          Decline reason
                          <textarea
                            maxLength={2000}
                            value={reason}
                            onChange={(event) => setReason(event.target.value)}
                          />
                        </label>
                        <button
                          className="ui-button primary"
                          disabled={!online || busy}
                          onClick={() => void decide(true)}
                        >
                          Accept
                        </button>
                        <button
                          className="ui-button"
                          disabled={!online || busy || !reason.trim()}
                          onClick={() => void decide(false)}
                        >
                          Decline
                        </button>
                      </>
                    )}
                  </>
                ) : (
                  requests.isSuccess && <p>This request is no longer available. Refresh Attention.</p>
                )}
              </>
            )}
            {error && (
              <Banner kind="error" label="Action failed">
                {error}
              </Banner>
            )}
          </div>
        </Drawer>
      )}
    </>
  );
}
