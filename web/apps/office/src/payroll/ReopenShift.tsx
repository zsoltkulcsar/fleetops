import { useId, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";
import { payrollApi, PayrollError } from "./api";

export function ReopenShift({
  id,
  version,
  onSaved,
}: {
  id: string;
  version: number;
  onSaved: () => Promise<void>;
}) {
  const session = useSession();
  const online = useOnline();
  const client = useQueryClient();
  const fieldId = useId();
  const [editing, setEditing] = useState(false);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  if (!session.data?.roles.some((role) => role === "hr" || role === "owner")) return null;
  if (!editing)
    return (
      <button className="ui-button" disabled={!online} onClick={() => setEditing(true)}>
        Re-open shift
      </button>
    );
  return (
    <form
      className="ts-detail"
      onSubmit={async (event) => {
        event.preventDefault();
        if (!reason.trim()) {
          setError("Reason is required.");
          return;
        }
        setBusy(true);
        setError("");
        try {
          await payrollApi.reopen(id, { version, reason: reason.trim() });
          await client.invalidateQueries({ queryKey: ["payroll"] });
          await onSaved();
        } catch (failure) {
          setError(
            failure instanceof PayrollError && failure.code === "export_changed"
              ? "The shift changed. Close and reopen the drawer to review its current version."
              : "The shift could not be reopened. Check your access and connection, then try again.",
          );
        } finally {
          setBusy(false);
        }
      }}
    >
      <h3>Re-open exported shift</h3>
      <p>
        Before sending, re-opening supersedes the export file. After sending, the revised shift goes into a
        correction export with per-day deltas.
      </p>
      <label htmlFor={fieldId}>Reason</label>
      <textarea
        id={fieldId}
        required
        maxLength={500}
        value={reason}
        onChange={(event) => setReason(event.target.value)}
      />
      {error && <p role="alert">{error}</p>}
      <button className="ui-button primary" disabled={busy || !online || !reason.trim()}>
        {busy ? "Saving..." : "Re-open shift"}
      </button>
      <button className="ui-button" type="button" disabled={busy} onClick={() => setEditing(false)}>
        Cancel
      </button>
    </form>
  );
}
