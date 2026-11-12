import { useId, useState, type FormEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import { Drawer } from "@fleetops/ui";
import { AuthRequestError } from "../auth/api";
import { readinessApi } from "../readiness/api";
import { fleetApi } from "../fleet/api";
import { scheduleApi, type ScheduleCycle } from "../schedule/api";
import { addDays, monday } from "./dates";

export function CycleEditor({
  mode,
  canEdit,
  presetVehicleId,
  onClose,
  onSaved,
}: {
  mode: ScheduleCycle | "new" | "pattern";
  canEdit: boolean;
  presetVehicleId?: string;
  onClose: () => void;
  onSaved: () => void;
}) {
  const fieldId = useId();
  const cycle = typeof mode === "object" ? mode : undefined;
  const patternMode = mode === "pattern";
  const [employeeId, setEmployeeId] = useState(cycle?.employeeId ?? "");
  const [failure, setFailure] = useState("");
  const [saving, setSaving] = useState(false);
  const drivers = useQuery({
    queryKey: ["readiness-drivers", false],
    queryFn: () => readinessApi.drivers(false),
  });
  const vehicles = useQuery({
    queryKey: ["schedule-trucks"],
    queryFn: () => fleetApi.list({ kind: "truck" }),
  });
  const pattern = useQuery({
    queryKey: ["cycle-pattern", employeeId],
    queryFn: () => scheduleApi.pattern(employeeId),
    enabled: patternMode && !!employeeId,
  });
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canEdit) return;
    const form = new FormData(event.currentTarget);
    setSaving(true);
    setFailure("");
    try {
      if (patternMode) {
        if (!pattern.data) throw new Error("pattern_not_loaded");
        await scheduleApi.savePattern(employeeId, {
          version: pattern.data.version,
          pattern: {
            onWeeks: Number(form.get("onWeeks")),
            offWeeks: Number(form.get("offWeeks")),
            anchorDate: String(form.get("anchorDate")),
            vehicleId: String(form.get("vehicleId")),
          },
        });
      } else {
        const body = {
          employeeId,
          vehicleId: String(form.get("vehicleId")),
          from: String(form.get("from")),
          to: String(form.get("to")),
          reason: String(form.get("reason")),
        };
        if (cycle) await scheduleApi.edit(cycle.id, { ...body, version: cycle.version });
        else await scheduleApi.add(body);
      }
      onSaved();
    } catch (error) {
      setFailure(
        error instanceof AuthRequestError && error.status === 409
          ? "The plan changed. Close this form and reload the plan before saving again."
          : "The plan could not be saved. Check the dates, driver and truck, then try again.",
      );
    } finally {
      setSaving(false);
    }
  };
  return (
    <Drawer
      open
      title={patternMode ? "Cycle pattern" : cycle ? "Edit cycle" : "Add cycle"}
      closeLabel="Close plan form"
      onClose={onClose}
    >
      <form className="schedule-form" onSubmit={submit}>
        {!canEdit && <p className="ui-muted">You can view this cycle. A dispatcher can change it.</p>}
        {cycle?.source === "generated" && (
          <p>Saving makes this cycle manual. Automatic rollover will keep this edit.</p>
        )}
        <div className="schedule-field">
          <label htmlFor={`${fieldId}-driver`}>Driver</label>
          <select
            id={`${fieldId}-driver`}
            aria-label="Driver"
            required
            value={employeeId}
            disabled={!canEdit || saving}
            onChange={(event) => setEmployeeId(event.target.value)}
          >
            <option value="">Choose a driver</option>
            {drivers.data?.items.map((driver) => (
              <option key={driver.id} value={driver.id}>
                {driver.name}
              </option>
            ))}
          </select>
        </div>
        {(drivers.isError || vehicles.isError) && (
          <p role="alert">Driver or truck choices could not be loaded. Close and try again.</p>
        )}
        {patternMode && pattern.isFetching && <p role="status">Loading pattern…</p>}
        {patternMode && pattern.isError && (
          <p role="alert">Pattern could not be loaded. Choose the driver again or retry later.</p>
        )}
        <div
          key={`${patternMode ? `${employeeId}:${pattern.data?.version}` : "cycle"}:${vehicles.isSuccess ? "trucks" : "loading"}`}
          className="schedule-form"
        >
          <div className="schedule-field">
            <label htmlFor={`${fieldId}-truck`}>Truck</label>
            <select
              id={`${fieldId}-truck`}
              aria-label="Truck"
              name="vehicleId"
              required
              defaultValue={cycle?.vehicleId ?? pattern.data?.pattern?.vehicleId ?? presetVehicleId ?? ""}
              disabled={!canEdit || saving}
            >
              <option value="">Choose a truck</option>
              {vehicles.data?.items.map((vehicle) => (
                <option key={vehicle.id} value={vehicle.id}>
                  {vehicle.plate}
                </option>
              ))}
            </select>
          </div>
          {patternMode ? (
            <>
              <div className="schedule-form-pair">
                <div className="schedule-field">
                  <label htmlFor={`${fieldId}-on-weeks`}>On weeks</label>
                  <input
                    id={`${fieldId}-on-weeks`}
                    name="onWeeks"
                    type="number"
                    min={1}
                    max={52}
                    required
                    defaultValue={pattern.data?.pattern?.onWeeks ?? 2}
                  />
                </div>
                <div className="schedule-field">
                  <label htmlFor={`${fieldId}-off-weeks`}>Off weeks</label>
                  <input
                    id={`${fieldId}-off-weeks`}
                    name="offWeeks"
                    type="number"
                    min={0}
                    max={52}
                    required
                    defaultValue={pattern.data?.pattern?.offWeeks ?? 2}
                  />
                </div>
              </div>
              <div className="schedule-field">
                <label htmlFor={`${fieldId}-anchor-date`}>Anchor date</label>
                <input
                  id={`${fieldId}-anchor-date`}
                  name="anchorDate"
                  type="date"
                  required
                  defaultValue={pattern.data?.pattern?.anchorDate ?? monday()}
                />
              </div>
              <p className="ui-muted">
                Keeps 12 weeks ahead. Future generated cycles change; manual cycles stay.
              </p>
            </>
          ) : (
            <>
              <div className="schedule-form-pair">
                <div className="schedule-field">
                  <label htmlFor={`${fieldId}-from`}>From</label>
                  <input
                    id={`${fieldId}-from`}
                    name="from"
                    type="date"
                    required
                    defaultValue={cycle?.from ?? monday()}
                    disabled={!canEdit || saving}
                  />
                </div>
                <div className="schedule-field">
                  <label htmlFor={`${fieldId}-to`}>To</label>
                  <input
                    id={`${fieldId}-to`}
                    name="to"
                    type="date"
                    required
                    defaultValue={cycle?.to ?? addDays(monday(), 13)}
                    disabled={!canEdit || saving}
                  />
                </div>
              </div>
              <div className="schedule-field">
                <label htmlFor={`${fieldId}-reason`}>Reason</label>
                <textarea
                  id={`${fieldId}-reason`}
                  name="reason"
                  required
                  maxLength={500}
                  disabled={!canEdit || saving}
                />
              </div>
            </>
          )}
        </div>
        {failure && (
          <p className="field-error" role="alert">
            {failure}
          </p>
        )}
        {canEdit && (
          <button
            className="ui-button primary"
            type="submit"
            disabled={
              saving ||
              !employeeId ||
              drivers.isPending ||
              vehicles.isPending ||
              (patternMode && !pattern.data)
            }
          >
            {saving ? "Saving…" : patternMode ? "Save pattern" : "Save cycle"}
          </button>
        )}
      </form>
    </Drawer>
  );
}
