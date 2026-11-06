import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Pencil } from "lucide-react";
import { Banner, Drawer, EmptyState, Panel, Plate, StatusPill, Toast } from "@fleetops/ui";
import { AuthRequestError } from "../auth/api";
import { useSession } from "../auth/AuthGate";
import { en } from "../i18n/en";
import { fleetApi, type ApiProblem, type RetiredReason, type VehicleDetail } from "./api";
import { VehicleForm } from "./VehicleForm";
import "./fleet.css";

const statusPillFor: Record<VehicleDetail["status"], "usable" | "off-road"> = {
  active: "usable",
  retired: "off-road",
};
const retiredReasonLabel: Record<RetiredReason, string> = {
  sold: en.fleet.retiredReason.sold,
  scrapped: en.fleet.retiredReason.scrapped,
  returned_lease: en.fleet.retiredReason.returned_lease,
};
function retiredDate(value: string): string {
  const date = new Date(value);
  return `${String(date.getUTCDate()).padStart(2, "0")}.${String(date.getUTCMonth() + 1).padStart(2, "0")}.${date.getUTCFullYear()}`;
}
export function FleetRecord({ id }: { id: string }) {
  const heading = useRef<HTMLHeadingElement>(null);
  const session = useSession();
  const query = useQuery({ queryKey: ["fleet", "vehicle", id], queryFn: () => fleetApi.detail(id) });
  const [tab, setTab] = useState("Overview");
  const [formOpen, setFormOpen] = useState(false);
  const [toast, setToast] = useState(false);
  const [retireOpen, setRetireOpen] = useState(false);
  const [undoOpen, setUndoOpen] = useState(false);
  const canEdit = Boolean(session.data?.roles.some((role) => role === "dispatcher" || role === "owner"));
  const isOwner = Boolean(session.data?.roles.some((role) => role === "owner"));
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
          {vehicle.status === "active" && canEdit && (
            <button className="ui-button" onClick={() => setRetireOpen(true)}>
              {en.fleet.retire.button}
            </button>
          )}
          {vehicle.status === "active" && canEdit && (
            <button className="ui-button" onClick={() => setFormOpen(true)}>
              <Pencil size={16} aria-hidden /> Edit
            </button>
          )}
        </div>
      </div>
      {vehicle.status === "retired" && vehicle.retiredReason && vehicle.retiredAt && (
        <Banner label={en.fleet.status.retired}>
          Retired: {retiredReasonLabel[vehicle.retiredReason]} on {retiredDate(vehicle.retiredAt)}.
        </Banner>
      )}
      {vehicle.status === "retired" &&
        isOwner &&
        vehicle.retiredAt &&
        Date.now() - new Date(vehicle.retiredAt).getTime() < 30 * 24 * 60 * 60 * 1000 && (
          <div className="record-retired-action">
            <button className="ui-button" onClick={() => setUndoOpen(true)}>
              {en.fleet.undoRetire.button}
            </button>
          </div>
        )}
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
      <RetireDrawer
        open={retireOpen}
        vehicle={vehicle}
        onClose={() => setRetireOpen(false)}
        onSaved={() => {
          setRetireOpen(false);
          setToast(true);
          void query.refetch();
        }}
      />
      <UndoRetireDrawer
        open={undoOpen}
        vehicle={vehicle}
        onClose={() => setUndoOpen(false)}
        onSaved={() => {
          setUndoOpen(false);
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

function RetireDrawer({
  open,
  vehicle,
  onClose,
  onSaved,
}: {
  open: boolean;
  vehicle: VehicleDetail;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [reason, setReason] = useState<RetiredReason | "">("");
  const [failure, setFailure] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const submit = async () => {
    setSaving(true);
    setFailure(null);
    if (!reason) {
      setFailure(en.fleet.retire.required);
      setSaving(false);
      return;
    }
    try {
      await fleetApi.retire(vehicle.id, { version: vehicle.version, retiredReason: reason });
      onSaved();
    } catch {
      setFailure(en.fleet.retire.failure);
    } finally {
      setSaving(false);
    }
  };
  return (
    <Drawer open={open} title={en.fleet.retire.title} onClose={onClose} closeLabel={en.actions.close}>
      {failure && (
        <Banner kind="error" label={en.banner.error}>
          {failure}
        </Banner>
      )}
      <div className="fleet-form">
        <label>
          {en.fleet.retire.reason}
          <select
            value={reason}
            onChange={(event) => {
              const value = event.target.value;
              if (value === "sold" || value === "scrapped" || value === "returned_lease") setReason(value);
            }}
          >
            <option value="">Select a reason</option>
            <option value="sold">{en.fleet.retire.sold}</option>
            <option value="scrapped">{en.fleet.retire.scrapped}</option>
            <option value="returned_lease">{en.fleet.retire.returnedLease}</option>
          </select>
        </label>
        <ul className="drawer-list">
          <li>{en.fleet.retire.activeList}</li>
          <li>{en.fleet.retire.readOnly}</li>
          <li>{en.fleet.retire.historyStays}</li>
        </ul>
        <div className="drawer-actions">
          <button type="button" className="ui-button" onClick={onClose}>
            {en.fleet.retire.cancel}
          </button>
          <button type="button" className="ui-button primary" disabled={saving} onClick={() => void submit()}>
            {en.fleet.retire.confirm}
          </button>
        </div>
      </div>
    </Drawer>
  );
}

function UndoRetireDrawer({
  open,
  vehicle,
  onClose,
  onSaved,
}: {
  open: boolean;
  vehicle: VehicleDetail;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [reason, setReason] = useState("");
  const [failure, setFailure] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const submit = async () => {
    if (!reason.trim()) {
      setFailure(en.fleet.undoRetire.required);
      return;
    }
    setSaving(true);
    setFailure(null);
    try {
      await fleetApi.undoRetire(vehicle.id, { version: vehicle.version, reason });
      onSaved();
    } catch (error) {
      const problem: ApiProblem = error instanceof AuthRequestError ? error.problem : {};
      setFailure(
        problem.code === "undo_window_closed"
          ? en.fleet.undoRetire.windowClosed
          : problem.code === "plate_in_use"
            ? en.fleet.undoRetire.plateInUse
            : en.fleet.undoRetire.failure,
      );
    } finally {
      setSaving(false);
    }
  };
  return (
    <Drawer open={open} title={en.fleet.undoRetire.title} onClose={onClose} closeLabel={en.actions.close}>
      {failure && (
        <Banner kind="error" label={en.banner.error}>
          {failure}
        </Banner>
      )}
      <form
        className="fleet-form"
        onSubmit={(event) => {
          event.preventDefault();
          void submit();
        }}
      >
        <label>
          {en.fleet.undoRetire.reason}
          <textarea value={reason} onChange={(event) => setReason(event.target.value)} required />
        </label>
        <div className="drawer-actions">
          <button type="button" className="ui-button" onClick={onClose}>
            {en.fleet.undoRetire.cancel}
          </button>
          <button type="submit" className="ui-button primary" disabled={saving}>
            {en.fleet.undoRetire.confirm}
          </button>
        </div>
      </form>
    </Drawer>
  );
}
