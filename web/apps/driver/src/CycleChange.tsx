import { useState } from "react";
import type { components } from "@fleetops/api-client";
import { Drawer } from "@fleetops/ui";
import { commandQueue, newCommand } from "./storage/commandQueue";
import type { DriverSnapshot } from "./storage/snapshot";
import { driverDate } from "./DriverSchedule";
import { usePlanningQueue } from "./usePlanningQueue";
import { requestStatusLabels } from "./planningLabels";

export function CycleChange({
  cycle,
  snapshot,
}: {
  cycle: components["schemas"]["DriverCycle"];
  snapshot: DriverSnapshot;
}) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const queue = usePlanningQueue();
  const requests = snapshot.scheduleRequests?.filter((request) => request.cycleId === cycle.id) ?? [];
  return (
    <div>
      {requests.map((request) => (
        <p key={request.id}>
          {driverDate(request.proposedFrom)} to {driverDate(request.proposedTo)}:{" "}
          {requestStatusLabels[request.status]}
          {request.declineReason ? `: ${request.declineReason}` : ""}
        </p>
      ))}
      {queue.commands
        .filter(
          (command) =>
            command.type === "schedule.request_change" &&
            command.payload &&
            typeof command.payload === "object" &&
            Reflect.get(command.payload, "cycleId") === cycle.id,
        )
        .map((command) => (
          <p key={command.commandId} role="status">
            {command.state === "waiting" ? "Waiting to sync" : "Request could not sync. Contact dispatch."}
          </p>
        ))}
      <button onClick={() => setOpen(true)}>Request change</button>
      {open && (
        <Drawer
          open
          title="Request change"
          closeLabel="Close change request"
          onClose={() => {
            if (!busy) setOpen(false);
          }}
        >
          <form
            className="driver-sheet"
            onSubmit={async (event) => {
              event.preventDefault();
              const form = new FormData(event.currentTarget);
              const from = String(form.get("from"));
              const to = String(form.get("to"));
              const note = String(form.get("note")).trim();
              if (from > to || !note) {
                setError("Check From, To and Note.");
                return;
              }
              setBusy(true);
              try {
                await commandQueue.enqueue(
                  newCommand("schedule.request_change", crypto.randomUUID(), cycle.version, {
                    cycleId: cycle.id,
                    proposedFrom: from,
                    proposedTo: to,
                    note,
                  }),
                );
                setOpen(false);
              } catch {
                setError("Request could not be saved. Try again.");
              } finally {
                setBusy(false);
              }
            }}
          >
            <label>
              New from
              <input type="date" name="from" required defaultValue={cycle.from} />
            </label>
            <label>
              New to
              <input type="date" name="to" required defaultValue={cycle.to} />
            </label>
            <label>
              Note
              <textarea name="note" required maxLength={2000} />
            </label>
            {(error || queue.error) && <p role="alert">{error || queue.error}</p>}
            <button className="primary" disabled={busy}>
              {busy ? "Saving\u2026" : "Send request"}
            </button>
          </form>
        </Drawer>
      )}
    </div>
  );
}
