import { useId, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Drawer } from "@fleetops/ui";
import type { components } from "@fleetops/api-client";
import { defectApi } from "./defectApi";
import { companyToday, defectDate, useDefectAccess } from "./defectPresentation";
import { useOnline } from "../shell/Shell";

export function AssignmentEditor({
  swap,
  onClose,
}: {
  swap: components["schemas"]["ScheduleSwap"];
  onClose: () => void;
}) {
  const id = useId();
  const access = useDefectAccess();
  const online = useOnline();
  const client = useQueryClient();
  const [from, setFrom] = useState(swap.from);
  const [to, setTo] = useState(swap.to ?? "");
  const [editing, setEditing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const save = async (end = false) => {
    setBusy(true);
    setError("");
    try {
      const result = end
        ? await defectApi.endAssignment(swap.id)
        : await defectApi.editAssignment(swap.id, { from, to: to || null });
      setFrom(result.from);
      setTo(result.to ?? "");
      setEditing(false);
      await client.invalidateQueries();
    } catch {
      setError("Swap could not be saved. Check the dates and try again.");
    } finally {
      setBusy(false);
    }
  };
  return (
    <Drawer open title={`Swap: ${swap.employeeName}`} closeLabel="Close swap" onClose={onClose}>
      <div className="fleet-form defect-form">
        <p>{swap.vehiclePlate}</p>
        {editing ? (
          <form
            className="defect-form"
            onSubmit={(event) => {
              event.preventDefault();
              void save();
            }}
          >
            <label htmlFor={`${id}-from`}>From</label>
            <input
              id={`${id}-from`}
              type="date"
              required
              value={from}
              disabled={busy || !online}
              onChange={(event) => setFrom(event.target.value)}
            />
            <label htmlFor={`${id}-to`}>To (optional)</label>
            <input
              id={`${id}-to`}
              type="date"
              min={from}
              value={to}
              disabled={busy || !online}
              onChange={(event) => setTo(event.target.value)}
            />
            <button className="ui-button primary" disabled={busy || !online}>
              Save
            </button>
            <button className="ui-button" type="button" disabled={busy} onClick={() => setEditing(false)}>
              Cancel
            </button>
          </form>
        ) : (
          <>
            <p>
              {defectDate(from)} to {to ? defectDate(to) : "Open ended"}
            </p>
            {access.canWrite && (
              <div className="defect-toolbar">
                <button className="ui-button" disabled={busy || !online} onClick={() => setEditing(true)}>
                  Edit swap
                </button>
                <button
                  className="ui-button"
                  disabled={busy || !online || (!!to && to < companyToday())}
                  onClick={() => void save(true)}
                >
                  End swap
                </button>
              </div>
            )}
          </>
        )}
        {swap.maintenanceId && <p>Linked to a repair. Ends when the repair is done.</p>}
        {error && <p role="alert">{error}</p>}
      </div>
    </Drawer>
  );
}
