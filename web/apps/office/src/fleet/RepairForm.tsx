import { useId, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Drawer } from "@fleetops/ui";
import { defectApi } from "./defectApi";
import { companyToday } from "./defectPresentation";
import { defectCategoryLabel, defectSeverityLabel } from "./defectLabels";
import { useOnline } from "../shell/Shell";

export function RepairForm({
  vehicleId,
  defectId,
  onClose,
  onSaved,
}: {
  vehicleId: string;
  defectId: string;
  onClose: () => void;
  onSaved: () => void;
}) {
  const id = useId();
  const client = useQueryClient();
  const online = useOnline();
  const [selected, setSelected] = useState([defectId]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const defects = useQuery({
    queryKey: ["repair-candidates", vehicleId],
    queryFn: () => defectApi.list({ vehicleId, statusGroup: "open", pageSize: 100 }),
  });
  return (
    <Drawer open title="Create repair" closeLabel="Close repair" onClose={onClose}>
      <form
        className="fleet-form defect-form"
        onSubmit={async (event) => {
          event.preventDefault();
          if (!online || busy) return;
          const form = new FormData(event.currentTarget);
          setBusy(true);
          setError("");
          try {
            await defectApi.createRepair({
              vehicleId,
              workshop: String(form.get("workshop")).trim(),
              scheduledFor: String(form.get("scheduledFor")),
              defectReportIds: selected,
            });
            await client.invalidateQueries();
            onSaved();
          } catch {
            setError("Repair could not be created. Check the fields and try again.");
          } finally {
            setBusy(false);
          }
        }}
      >
        <label htmlFor={`${id}-workshop`}>Workshop</label>
        <input id={`${id}-workshop`} name="workshop" required maxLength={200} disabled={busy || !online} />
        <label htmlFor={`${id}-date`}>Scheduled date</label>
        <input
          id={`${id}-date`}
          name="scheduledFor"
          type="date"
          required
          defaultValue={companyToday()}
          disabled={busy || !online}
        />
        <fieldset disabled={busy || !online}>
          <legend>Linked defects on this vehicle</legend>
          {defects.data?.items.map((defect) => (
            <div key={defect.id}>
              <input
                id={`${id}-${defect.id}`}
                type="checkbox"
                checked={selected.includes(defect.id)}
                onChange={(event) =>
                  setSelected((previous) =>
                    event.target.checked
                      ? [...previous, defect.id]
                      : previous.filter((value) => value !== defect.id),
                  )
                }
              />
              <label htmlFor={`${id}-${defect.id}`}>
                {defectCategoryLabel(defect.category)} ({defectSeverityLabel(defect.severity)})
              </label>
            </div>
          ))}
        </fieldset>
        {defects.isError && <p role="alert">Other defects could not be loaded.</p>}
        {error && <p role="alert">{error}</p>}
        <button className="ui-button primary" disabled={busy || !online || !selected.length}>
          Create repair
        </button>
      </form>
    </Drawer>
  );
}
