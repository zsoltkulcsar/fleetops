import { useId, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Drawer } from "@fleetops/ui";
import { apiClient, type components } from "@fleetops/api-client";
import { checked, defectApi } from "./defectApi";
import { companyToday } from "./defectPresentation";

export const maintenanceTypes = [
  "service",
  "repair",
  "inspection_hu",
  "inspection_sp",
  "tacho_check",
  "tyres",
  "other",
];
export const maintenanceLabel = (type: string) =>
  (
    ({
      service: "Service",
      repair: "Repair",
      inspection_hu: "HU inspection",
      inspection_sp: "SP inspection",
      tacho_check: "Tacho check",
      tyres: "Tyres",
      other: "Other",
    }) as Record<string, string>
  )[type] ?? type;
export function MaintenancePlan({
  vehicleId,
  type = "service",
  proposal,
  repair,
  onClose,
}: {
  vehicleId?: string;
  type?: string;
  repair?: components["schemas"]["RepairDetail"];
  proposal?: components["schemas"]["NextServiceProposal"];
  onClose: () => void;
}) {
  const id = useId();
  const client = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const vehicles = useQuery({
    queryKey: ["maintenance-vehicles"],
    queryFn: async () => {
      const items: components["schemas"]["VehicleListItem"][] = [];
      let cursor: string | undefined;
      do {
        const page = await apiClient
          .GET("/api/v1/fleet/vehicles", { params: { query: { cursor, limit: 100 } } })
          .then(checked);
        items.push(...page.items);
        cursor = page.nextCursor ?? undefined;
      } while (cursor);
      return items;
    },
  });
  return (
    <Drawer
      open
      title={proposal ? "Plan next service" : "Plan maintenance"}
      closeLabel="Close plan"
      onClose={onClose}
    >
      <form
        className="fleet-form defect-form"
        onSubmit={async (event) => {
          event.preventDefault();
          const form = new FormData(event.currentTarget);
          if (!form.get("dueDate") && !form.get("dueKm")) {
            setError("Enter a due date or due km.");
            return;
          }
          setBusy(true);
          setError("");
          try {
            const body = {
              vehicleId: String(form.get("vehicleId")),
              type: String(form.get("type")),
              scheduledFor: String(form.get("scheduledFor")),
              workshop: String(form.get("workshop")).trim() || null,
              dueDate: String(form.get("dueDate")) || null,
              dueKm: form.get("dueKm") ? Number(form.get("dueKm")) : null,
            };
            if (repair)
              await apiClient
                .PUT("/api/v1/fleet/maintenance/{id}", { params: { path: { id: repair.id } }, body })
                .then(checked);
            else await defectApi.createRepair(body);
            await client.invalidateQueries();
            onClose();
          } catch {
            setError("Maintenance could not be saved. Check the fields and try again.");
          } finally {
            setBusy(false);
          }
        }}
      >
        <fieldset disabled={busy} className="defect-form">
          <label htmlFor={id + "-vehicle"}>Vehicle</label>
          <select
            id={id + "-vehicle"}
            name="vehicleId"
            required
            defaultValue={repair?.vehicleId ?? proposal?.vehicleId ?? vehicleId ?? ""}
          >
            <option value="">Choose a vehicle</option>
            {vehicles.data?.map((vehicle) => (
              <option key={vehicle.id} value={vehicle.id}>
                {vehicle.plate}
              </option>
            ))}
          </select>
          {vehicles.isError && <p role="alert">Vehicles could not be loaded.</p>}
          <label htmlFor={id + "-type"}>Type</label>
          <select id={id + "-type"} name="type" defaultValue={repair?.type ?? proposal?.type ?? type}>
            {maintenanceTypes.map((value) => (
              <option key={value} value={value}>
                {maintenanceLabel(value)}
              </option>
            ))}
          </select>
          <label htmlFor={id + "-due"}>Due date</label>
          <input
            id={id + "-due"}
            name="dueDate"
            type="date"
            defaultValue={repair?.dueDate ?? proposal?.dueDate ?? ""}
          />
          <label htmlFor={id + "-km"}>Due km</label>
          <input
            id={id + "-km"}
            name="dueKm"
            type="number"
            min={0}
            max={2147483647}
            step={1}
            defaultValue={repair?.dueKm ?? proposal?.dueKm ?? ""}
          />
          <label htmlFor={id + "-scheduled"}>Scheduled for</label>
          <input
            id={id + "-scheduled"}
            name="scheduledFor"
            type="date"
            required
            defaultValue={repair?.scheduledFor ?? proposal?.dueDate ?? companyToday()}
          />
          <label htmlFor={id + "-workshop"}>Workshop</label>
          <input
            id={id + "-workshop"}
            name="workshop"
            defaultValue={repair?.workshop ?? ""}
            maxLength={200}
          />
        </fieldset>
        {error && <p role="alert">{error}</p>}
        <button className="ui-button primary" disabled={busy || vehicles.isPending || vehicles.isError}>
          Save
        </button>
        <button className="ui-button" type="button" disabled={busy} onClick={onClose}>
          {proposal ? "Not now" : "Cancel"}
        </button>
      </form>
    </Drawer>
  );
}
