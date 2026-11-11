import { completeMaintenance, resultTypeFor } from "./completeMaintenance";
import { useSearchParams } from "react-router";
import { MaintenancePlan, maintenanceLabel, maintenanceTypes } from "./MaintenancePlan";
import { useEffect, useId, useState } from "react";
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
  const [params, setParams] = useSearchParams();
  const [planning, setPlanning] = useState(params.get("action") === "plan");
  const [proposal, setProposal] = useState<components["schemas"]["NextServiceProposal"]>();
  const [type, setType] = useState("");
  const [overdue, setOverdue] = useState(false);
  const [filterVehicle, setFilterVehicle] = useState(vehicleId ?? params.get("vehicleId") ?? "");
  const access = useDefectAccess();
  const online = useOnline();
  const client = useQueryClient();
  const [status, setStatus] = useState<components["schemas"]["MaintenanceStatus"] | "">("");
  const [editing, setEditing] = useState<{
    repair: components["schemas"]["RepairDetail"];
    action: "view" | "done" | "cancel";
  }>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const query = useQuery({
    queryKey: ["maintenance", filterVehicle, status],
    queryFn: () => defectApi.repairs(filterVehicle || undefined, status || undefined),
    enabled: access.canRead,
  });
  useEffect(() => {
    if (params.get("action") !== "done" || !query.data || !access.canWrite) return;
    const repair = query.data.items.find(
      (item) =>
        !["done", "cancelled"].includes(item.status) &&
        (!params.get("maintenanceId") || item.id === params.get("maintenanceId")),
    );
    if (repair) setEditing({ repair, action: "done" });
    setParams(
      (previous) => {
        previous.delete("action");
        previous.delete("maintenanceId");
        return previous;
      },
      { replace: true },
    );
  }, [query.data, params, access.canWrite, setParams]);
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
      const result = await operation();
      // Close the editing drawer before the next-service proposal opens: never two dialogs at once.
      setEditing(undefined);
      if (result && typeof result === "object" && "nextService" in result && result.nextService) {
        setProposal(result.nextService as components["schemas"]["NextServiceProposal"]);
        setPlanning(true);
      }
      await client.invalidateQueries();
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
        {access.canWrite && (
          <button className="ui-button primary" disabled={!online} onClick={() => setPlanning(true)}>
            Plan maintenance
          </button>
        )}
        {!vehicleId && (
          <>
            <label htmlFor={id + "-vehicle-filter"}>Vehicle</label>
            <select
              id={id + "-vehicle-filter"}
              value={filterVehicle}
              onChange={(event) => setFilterVehicle(event.target.value)}
            >
              <option value="">All vehicles</option>
              {Array.from(vehicles.data ?? []).map(([key, plate]) => (
                <option key={key} value={key}>
                  {plate}
                </option>
              ))}
            </select>
          </>
        )}
        <label htmlFor={id + "-type-filter"}>Type</label>
        <select id={id + "-type-filter"} value={type} onChange={(event) => setType(event.target.value)}>
          <option value="">All types</option>
          {maintenanceTypes.map((value) => (
            <option key={value} value={value}>
              {maintenanceLabel(value)}
            </option>
          ))}
        </select>
        <label htmlFor={id + "-overdue"}>Overdue only</label>
        <input
          id={id + "-overdue"}
          type="checkbox"
          checked={overdue}
          onChange={(event) => setOverdue(event.target.checked)}
        />
        <label htmlFor={`${id}-status`}>Status</label>
        <select
          id={`${id}-status`}
          value={status}
          onChange={(event) => setStatus(event.target.value as typeof status)}
        >
          <option value="">All maintenance</option>
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
                {[
                  "Vehicle",
                  "Type",
                  "Workshop",
                  "Scheduled",
                  "Due / km",
                  "Status",
                  "Linked defects",
                  "Actions",
                ].map((heading) => (
                  <th key={heading} scope="col">
                    {heading}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {query.data.items
                .filter((repair) => (!type || repair.type === type) && (!overdue || repair.overdue))
                .map((repair) => (
                  <tr key={repair.id}>
                    <td>{vehicles.data?.get(repair.vehicleId) ?? "Loading vehicle..."}</td>
                    <td>
                      <button
                        className="ui-button ghost"
                        onClick={() => setEditing({ repair, action: "view" })}
                      >
                        {maintenanceLabel(repair.type)}
                      </button>
                    </td>
                    <td>{repair.workshop}</td>
                    <td>{defectDate(repair.scheduledFor)}</td>
                    <td>
                      {repair.dueDate && <p>{defectDate(repair.dueDate)}</p>}
                      {repair.dueKm != null && <p>{repair.dueKm} km</p>}
                      {repair.overdue && <strong>Overdue</strong>}
                      <p>
                        {repair.odometerKm ?? "Unknown"} km
                        {repair.kmAsOf ? " as of " + defectDate(repair.kmAsOf).slice(0, 6) : " (no reading)"}
                      </p>
                      {repair.kmReadingStale && <p>km reading older than 30 days</p>}
                    </td>
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
      {planning && access.canWrite && (!params.get("maintenanceId") || query.isSuccess) && (
        <MaintenancePlan
          vehicleId={vehicleId ?? filterVehicle}
          type={params.get("type") ?? "service"}
          proposal={proposal}
          repair={query.data?.items.find((item) => item.id === params.get("maintenanceId"))}
          onClose={() => {
            setPlanning(false);
            setProposal(undefined);
            setParams(
              (previous) => {
                previous.delete("action");
                previous.delete("maintenanceId");
                return previous;
              },
              { replace: true },
            );
          }}
        />
      )}
      {editing && (
        <Drawer
          open
          title={
            editing.action === "view"
              ? "Maintenance item"
              : editing.action === "done"
                ? "Mark maintenance done"
                : "Cancel maintenance"
          }
          closeLabel="Close maintenance"
          onClose={() => setEditing(undefined)}
        >
          <form
            className="fleet-form defect-form"
            onSubmit={(event) => {
              event.preventDefault();
              if (editing.action === "view" || !access.canWrite || !online || busy) return;
              const form = new FormData(event.currentTarget);
              void save(() =>
                editing.action === "cancel"
                  ? defectApi.cancelRepair(editing.repair.id, { reason: String(form.get("reason")).trim() })
                  : completeMaintenance(editing.repair, form),
              );
            }}
          >
            {editing.action === "view" ? (
              <>
                <p>
                  {maintenanceLabel(editing.repair.type)}: {editing.repair.workshop}
                </p>
                <p>{defectDate(editing.repair.scheduledFor)}</p>
                <DefectState value={editing.repair.status} />
                {editing.repair.kmReadingStale && <p>km reading older than 30 days</p>}
                {access.canWrite && !["done", "cancelled"].includes(editing.repair.status) && (
                  <div className="defect-toolbar">
                    {editing.repair.status === "scheduled" && (
                      <button
                        type="button"
                        className="ui-button"
                        disabled={busy || !online}
                        onClick={() => void save(() => defectApi.startRepair(editing.repair.id))}
                      >
                        Start
                      </button>
                    )}
                    <button
                      type="button"
                      className="ui-button"
                      onClick={() => setEditing({ ...editing, action: "done" })}
                    >
                      Done
                    </button>
                    <button
                      type="button"
                      className="ui-button"
                      onClick={() => setEditing({ ...editing, action: "cancel" })}
                    >
                      Cancel
                    </button>
                  </div>
                )}
              </>
            ) : editing.action === "cancel" ? (
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
                <label htmlFor={id + "-invoice"}>Invoice upload</label>
                <input
                  id={id + "-invoice"}
                  name="invoice"
                  type="file"
                  accept="application/pdf,image/jpeg,image/png"
                  disabled={busy || !online}
                />
                {resultTypeFor(editing.repair.type) && (
                  <>
                    <label htmlFor={id + "-result"}>Result document upload</label>
                    <input
                      id={id + "-result"}
                      name="result"
                      type="file"
                      required
                      accept="application/pdf,image/jpeg,image/png"
                      disabled={busy || !online}
                    />
                    <label htmlFor={id + "-expiry"}>Expiry date</label>
                    <input
                      id={id + "-expiry"}
                      name="expiresAt"
                      type="date"
                      required
                      disabled={busy || !online}
                    />
                  </>
                )}
                <label htmlFor={`${id}-done`}>Done date</label>
                <input
                  id={`${id}-done`}
                  type="date"
                  name="doneAt"
                  required
                  defaultValue={companyToday()}
                  disabled={busy || !online}
                />
                <label htmlFor={id + "-cost"}>Cost (EUR)</label>
                <input
                  id={id + "-cost"}
                  name="cost"
                  type="number"
                  min={0}
                  step="0.01"
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
            {editing.action !== "view" && (
              <button className="ui-button primary" disabled={busy || !online}>
                Save
              </button>
            )}
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
      <Panel title="Maintenance">{content}</Panel>
    </div>
  );
}
