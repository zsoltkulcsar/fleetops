import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Pencil } from "lucide-react";
import { Banner, EmptyState, Panel, Plate, StatusPill, Toast } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { en } from "../i18n/en";
import { fleetApi, type VehicleDetail } from "./api";
import { VehicleForm } from "./VehicleForm";
import "./fleet.css";

const statusPillFor: Record<VehicleDetail["status"], "usable" | "off-road"> = {
  active: "usable",
  retired: "off-road",
};
export function FleetRecord({ id }: { id: string }) {
  const heading = useRef<HTMLHeadingElement>(null);
  const session = useSession();
  const query = useQuery({ queryKey: ["fleet", "vehicle", id], queryFn: () => fleetApi.detail(id) });
  const [tab, setTab] = useState("Overview");
  const [formOpen, setFormOpen] = useState(false);
  const [toast, setToast] = useState(false);
  const canEdit = Boolean(session.data?.roles.some((role) => role === "dispatcher" || role === "owner"));
  useEffect(() => {
    if (query.data) document.title = `${query.data.plate} · ${en.appName}`;
  }, [query.data]);
  if (query.isLoading)
    return (
      <div className="page fleet-page">
        <div className="fleet-loading">Loading vehicle record…</div>
      </div>
    );
  if (query.error && (query.error as { status?: number }).status === 404)
    return (
      <div className="page fleet-page">
        <Panel>
          <EmptyState heading="Vehicle not found">
            This vehicle may have been removed or you may not have access to it.
          </EmptyState>
        </Panel>
      </div>
    );
  if (query.isError || !query.data)
    return (
      <div className="page fleet-page">
        <Banner label="Error" retryLabel="Try again" onRetry={() => void query.refetch()}>
          Data could not be loaded. Check the connection and try again.
        </Banner>
      </div>
    );
  const vehicle = query.data;
  return (
    <div className="page fleet-page">
      <div className="record-back">
        <button className="ui-button ghost" onClick={() => window.history.back()}>
          <ArrowLeft size={16} aria-hidden /> Back to Fleet
        </button>
      </div>
      <div className="record-header">
        <div>
          <span className="eyebrow">Vehicle record</span>
          <h1 ref={heading} tabIndex={-1}>
            <Plate>{vehicle.plate}</Plate>{" "}
            <span>
              {vehicle.make} {vehicle.model}
            </span>
          </h1>
        </div>
        <div className="record-actions">
          <StatusPill status={statusPillFor[vehicle.status]} label={en.fleet.status[vehicle.status]} />
          {canEdit && (
            <button className="ui-button" onClick={() => setFormOpen(true)}>
              <Pencil size={16} aria-hidden /> Edit
            </button>
          )}
        </div>
      </div>
      <div className="fleet-tabs" role="tablist" aria-label="Vehicle record sections">
        {["Overview", "Documents", "Maintenance", "Defects", "Drivers", "History"].map((name) => (
          <button
            key={name}
            role="tab"
            aria-selected={tab === name}
            className={tab === name ? "selected" : ""}
            onClick={() => setTab(name)}
          >
            {name}
          </button>
        ))}
      </div>
      {tab === "Overview" && (
        <Panel title="Overview">
          <dl className="vehicle-details">
            {[
              ["VIN", vehicle.vin],
              ["Year", vehicle.year],
              ["Gross weight", vehicle.grossWeightKg ? `${vehicle.grossWeightKg} kg` : "Not set"],
              ["Licence class", vehicle.requiredLicenceClass],
              ["Ownership", en.fleet.ownership[vehicle.ownership]],
              ["Odometer", vehicle.odometerKm ? `${vehicle.odometerKm.toLocaleString()} km` : "Not set"],
              [
                "Service interval",
                vehicle.serviceIntervalKm ? `${vehicle.serviceIntervalKm.toLocaleString()} km` : "Not set",
              ],
            ].map(([name, value]) => (
              <div key={name}>
                <dt>{name}</dt>
                <dd>{value ?? "Not set"}</dd>
              </div>
            ))}
          </dl>
        </Panel>
      )}
      {tab === "Documents" && (
        <Panel title="Required documents">
          <ul className="history-list">
            {vehicle.requiredDocuments.map((document) => (
              <li key={document.type}>
                <span>{document.type}</span>
                <span>
                  {document.needsConfirmation
                    ? en.fleet.confirmationRequired
                    : en.fleet.confirmationNotRequired}
                </span>
              </li>
            ))}
          </ul>
        </Panel>
      )}
      {tab === "History" && (
        <Panel title="Plate history">
          {vehicle.plateHistory?.length ? (
            <ul className="history-list">
              {vehicle.plateHistory.map((plate) => (
                <li key={plate}>
                  <Plate>{plate}</Plate>
                </li>
              ))}
            </ul>
          ) : (
            <EmptyState heading="No plate history">Plate changes will appear here for audit.</EmptyState>
          )}
        </Panel>
      )}
      {!["Overview", "History"].includes(tab) && (
        <Panel title={tab}>
          <EmptyState heading={`${tab} coming later`}>
            {tab} will be filled by its own FleetOps feature.
          </EmptyState>
        </Panel>
      )}
      <VehicleForm
        open={formOpen}
        vehicle={vehicle}
        onClose={() => setFormOpen(false)}
        onSaved={() => {
          setFormOpen(false);
          setToast(true);
          void query.refetch();
        }}
      />
      <Toast open={toast} onClose={() => setToast(false)} dismissLabel="Dismiss" undoLabel="Undo">
        Vehicle saved successfully.
      </Toast>
    </div>
  );
}
