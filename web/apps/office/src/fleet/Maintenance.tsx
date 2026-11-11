import { useId, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Banner, Drawer, Panel } from "@fleetops/ui";
import { apiClient, type components } from "@fleetops/api-client";
import { checked, defectApi } from "./defectApi";
import { DefectState, companyToday, defectDate, useDefectAccess } from "./defectPresentation";
import { FleetTabs } from "./FleetTabs";
import { useOnline } from "../shell/Shell";
import "./defects.css";

export function Maintenance({ vehicleId }: { vehicleId?: string }) {
  const id = useId();
  const access = useDefectAccess();
  const online = useOnline();
  const client = useQueryClient();
  const [status, setStatus] = useState<components["schemas"]["MaintenanceStatus"] | "">("");
  const [editing, setEditing] = useState<{
    repair: components["schemas"]["RepairDetail"];
    action: "done" | "cancel";
  }>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const query = useQuery({
    queryKey: ["maintenance", vehicleId, status],
    queryFn: () => defectApi.repairs(vehicleId, status || undefined),
    enabled: access.canRead,
  });
  const vehicles = useQuery({
    queryKey: ["maintenance-plates"],
    queryFn: async () => {
      const result = new Map<string, string>();
      let cursor: string | undefined;
      do {
        const page = await apiClient
          .GET("/api/v1/fleet/vehicles", { params: { query: { cursor, limit: 100, includeRetired: true } } })
          .then(checked);
        page.items.forEach((vehicle) => result.set(vehicle.id, vehicle.plate));
        cursor = page.nextCursor ?? undefined;
      } while (cursor);
      return result;
    },
    enabled: access.canRead,
  });
  const save = async (operation: () => Promise<unknown>) => {
    setBusy(true);
    setError("");
    try {
      await operation();
      await client.invalidateQueries();
      setEditing(undefined);
    } catch {
      setError("Repair could not be saved. Check the fields and try again.");
    } finally {
      setBusy(false);
    }
  };
  const content = !access.canRead ? (
    <p>Ask an owner to grant access in Settings, Users &amp; roles.</p>
  ) : (
    <>
      {!online && (
        <Banner kind="offline" label="Offline">
          Showing saved data. Changes need a connection.
        </Banner>
      )}
      <div className="defect-toolbar">
        <label htmlFor={`${id}-status`}>Status</label>
        <select
          id={`${id}-status`}
          value={status}
          onChange={(event) => setStatus(event.target.value as typeof status)}
        >
          <option value="">All repairs</option>
          <option value="scheduled">Scheduled</option>
          <option value="in_progress">In progress</option>
          <option value="done">Done</option>
          <option value="cancelled">Cancelled</option>
        </select>
      </div>
      {(query.isError || vehicles.isError) && (
        <Banner
          kind="error"
          label="Error"
          retryLabel="Try again"
          onRetry={() => {
            void query.refetch();
            void vehicles.refetch();
          }}
        >
          Data could not be loaded. Check the connection and try again.
        </Banner>
      )}
      {query.isPending ? (
        <p role="status">Loading maintenance...</p>
      ) : query.data?.items.length ? (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                {["Vehicle", "Type", "Workshop", "Scheduled", "Status", "Linked defects", "Actions"].map(
                  (heading) => (
                    <th key={heading} scope="col">
                      {heading}
                    </th>
                  ),
                )}
              </tr>
            </thead>
            <tbody>
              {query.data.items.map((repair) => (
                <tr key={repair.id}>
                  <td>{vehicles.data?.get(repair.vehicleId) ?? "Loading vehicle..."}</td>
                  <td>Repair</td>
                  <td>{repair.workshop}</td>
                  <td>{defectDate(repair.scheduledFor)}</td>
                  <td>
                    <DefectState value={repair.status} />
                    {repair.inWorkshop && <span>In workshop</span>}
                    {repair.cancelReason && <p>{repair.cancelReason}</p>}
                    {repair.doneAt && (
                      <p>
                        Done {defectDate(repair.doneAt)}
                        {repair.odometerAtDoneKm != null ? `, ${repair.odometerAtDoneKm} km` : ""}
                      </p>
                    )}
                  </td>
                  <td>{repair.defectReportIds.length}</td>
                  <td>
                    {access.canWrite && !["done", "cancelled"].includes(repair.status) && (
                      <div className="defect-toolbar">
                        {repair.status === "scheduled" && (
                          <button
                            className="ui-button"
                            disabled={busy || !online}
                            onClick={() => void save(() => defectApi.startRepair(repair.id))}
                          >
                            Start
                          </button>
                        )}
                        <button
                          className="ui-button"
                          disabled={busy || !online}
                          onClick={() => setEditing({ repair, action: "done" })}
                        >
                          Done
                        </button>
                        <button
                          className="ui-button"
                          disabled={busy || !online}
                          onClick={() => setEditing({ repair, action: "cancel" })}
                        >
                          Cancel
                        </button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        query.data && (
          <p className="defect-empty">
            No maintenance planned. Set service intervals on each vehicle to get reminders.
          </p>
        )
      )}
      {error && <p role="alert">{error}</p>}
      {editing && (
        <Drawer
          open
          title={editing.action === "done" ? "Mark repair done" : "Cancel repair"}
          closeLabel="Close repair"
          onClose={() => setEditing(undefined)}
        >
          <form
            className="fleet-form defect-form"
            onSubmit={(event) => {
              event.preventDefault();
              const form = new FormData(event.currentTarget);
              void save(() =>
                editing.action === "cancel"
                  ? defectApi.cancelRepair(editing.repair.id, { reason: String(form.get("reason")).trim() })
                  : defectApi.doneRepair(editing.repair.id, {
                      doneAt: String(form.get("doneAt")),
                      odometerAtDoneKm: form.get("odometer") ? Number(form.get("odometer")) : null,
                    }),
              );
            }}
          >
            {editing.action === "cancel" ? (
              <>
                <label htmlFor={`${id}-reason`}>Reason</label>
                <textarea
                  id={`${id}-reason`}
                  name="reason"
                  required
                  maxLength={2000}
                  disabled={busy || !online}
                />
              </>
            ) : (
              <>
                <label htmlFor={`${id}-done`}>Done date</label>
                <input
                  id={`${id}-done`}
                  type="date"
                  name="doneAt"
                  required
                  defaultValue={companyToday()}
                  disabled={busy || !online}
                />
                <label htmlFor={`${id}-odometer`}>Odometer (km, optional)</label>
                <input
                  id={`${id}-odometer`}
                  type="number"
                  min={0}
                  max={2147483647}
                  step={1}
                  name="odometer"
                  disabled={busy || !online}
                />
              </>
            )}
            {error && <p role="alert">{error}</p>}
            <button className="ui-button primary" disabled={busy || !online}>
              Save
            </button>
          </form>
        </Drawer>
      )}
    </>
  );
  return vehicleId ? (
    <Panel title="Maintenance">{content}</Panel>
  ) : (
    <div className="page fleet-page">
      <div className="page-title">
        <h1 tabIndex={-1}>Fleet {"\u00b7"} Maintenance</h1>
      </div>
      <FleetTabs />
      <Panel title="Repairs">{content}</Panel>
    </div>
  );
}
