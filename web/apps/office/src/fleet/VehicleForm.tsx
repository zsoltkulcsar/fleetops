import { useState, type FormEvent } from "react";
import { Banner, Drawer } from "@fleetops/ui";
import { AuthRequestError } from "../auth/api";
import { fleetApi, type ApiProblem, type VehicleDetail, type VehicleInput } from "./api";

const fields = [
  ["plate", "Plate", "text"],
  ["vin", "VIN", "text"],
  ["make", "Make", "text"],
  ["model", "Model", "text"],
  ["year", "Year", "number"],
  ["grossWeightKg", "Gross weight (kg)", "number"],
  ["requiredLicenceClass", "Required licence class", "text"],
  ["leaseEndDate", "Lease end date", "date"],
  ["odometerKm", "Odometer (km)", "number"],
  ["serviceIntervalKm", "Service interval (km)", "number"],
  ["serviceIntervalMonths", "Service interval (months)", "number"],
  ["lastTachoDownloadAt", "Last tacho download", "date"],
] as const;
function initialValue(vehicle?: VehicleDetail) {
  return Object.fromEntries(
    fields.map(([name]) => [name, String(vehicle?.[name as keyof VehicleDetail] ?? "")]),
  ) as Record<string, string>;
}

export function VehicleForm({
  open,
  vehicle,
  onClose,
  onSaved,
}: {
  open: boolean;
  vehicle?: VehicleDetail;
  onClose: () => void;
  onSaved: (vehicle: VehicleDetail) => void;
}) {
  const [values, setValues] = useState(() => initialValue(vehicle));
  const [kind, setKind] = useState(String(vehicle?.kind ?? "truck"));
  const [ownership, setOwnership] = useState(String(vehicle?.ownership ?? "owned"));
  const [reason, setReason] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [conflict, setConflict] = useState(false);
  const [saving, setSaving] = useState(false);
  const isEdit = Boolean(vehicle);
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setErrors({});
    setConflict(false);
    if (isEdit && !reason.trim()) {
      setErrors({ reason: "Reason is required when editing a vehicle." });
      return;
    }
    const input: VehicleInput = {
      ...values,
      kind,
      ownership,
      year: Number(values.year),
      grossWeightKg: Number(values.grossWeightKg),
      odometerKm: values.odometerKm ? Number(values.odometerKm) : null,
      serviceIntervalKm: values.serviceIntervalKm ? Number(values.serviceIntervalKm) : null,
      serviceIntervalMonths: values.serviceIntervalMonths ? Number(values.serviceIntervalMonths) : null,
      reason: isEdit ? reason : null,
    };
    setSaving(true);
    try {
      const saved = isEdit
        ? await fleetApi.update(vehicle!.id, { ...input, version: vehicle!.version })
        : await fleetApi.create(input);
      onSaved(saved);
    } catch (error) {
      const requestError = error as AuthRequestError;
      const problem = requestError.problem as ApiProblem;
      if (requestError.status === 409 && problem.code === "version_conflict") setConflict(true);
      if (requestError.status === 400 && problem.errors) {
        const nextErrors: Record<string, string> = {};
        for (const [name, messages] of Object.entries(problem.errors))
          nextErrors[name] = Array.isArray(messages) ? messages[0] : messages;
        setErrors(nextErrors);
      }
    } finally {
      setSaving(false);
    }
  };
  return (
    <Drawer
      open={open}
      title={isEdit ? `Edit ${vehicle?.plate ?? "vehicle"}` : "Add vehicle"}
      onClose={onClose}
      closeLabel="Close"
    >
      {conflict && (
        <Banner kind="error" label="Version conflict">
          This vehicle changed elsewhere. Review your input and try again.
        </Banner>
      )}
      <form className="fleet-form" onSubmit={submit}>
        <label>
          Kind
          <select value={kind} onChange={(event) => setKind(event.target.value)}>
            <option value="truck">Truck</option>
            <option value="trailer">Trailer</option>
          </select>
        </label>
        <label>
          Ownership
          <select value={ownership} onChange={(event) => setOwnership(event.target.value)}>
            <option value="owned">Owned</option>
            <option value="leased">Leased</option>
            <option value="rented">Rented</option>
          </select>
        </label>
        {fields.map(([name, label, type]) => (
          <label key={name}>
            {label}
            <input
              type={type}
              value={values[name]}
              onChange={(event) => setValues((current) => ({ ...current, [name]: event.target.value }))}
              aria-invalid={Boolean(errors[name])}
            />
            {errors[name] && <span className="field-error">{errors[name]}</span>}
          </label>
        ))}
        {isEdit && (
          <label>
            Reason
            <textarea
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              aria-invalid={Boolean(errors.reason)}
            />
            {errors.reason && <span className="field-error">{errors.reason}</span>}
          </label>
        )}
        <button className="ui-button primary" type="submit" disabled={saving}>
          {saving ? "Saving…" : isEdit ? "Save changes" : "Add vehicle"}
        </button>
      </form>
    </Drawer>
  );
}
