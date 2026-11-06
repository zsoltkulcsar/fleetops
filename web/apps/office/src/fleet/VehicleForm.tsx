import { useState, type FormEvent } from "react";
import type { components } from "@fleetops/api-client";
import { Banner, Drawer } from "@fleetops/ui";
import { AuthRequestError } from "../auth/api";
import { en } from "../i18n/en";
import { fleetApi, type ApiProblem, type VehicleDetail, type VehicleInput } from "./api";

type VehicleKind = components["schemas"]["VehicleKind"];
type VehicleOwnership = components["schemas"]["VehicleOwnership"];
type TrailerType = Exclude<components["schemas"]["TrailerType"], null>;
type FormValues = {
  plate: string;
  vin: string;
  make: string;
  model: string;
  year: string;
  grossWeightKg: string;
  requiredLicenceClass: string;
  leaseEndDate: string;
  odometerKm: string;
  depotId: string;
  serviceIntervalKm: string;
  serviceIntervalMonths: string;
  lastTachoDownloadAt: string;
};
type Field = { name: keyof FormValues; label: string; type: "text" | "number" | "date" };
const fields: readonly Field[] = [
  { name: "plate", label: "Plate", type: "text" },
  { name: "vin", label: "VIN", type: "text" },
  { name: "make", label: "Make", type: "text" },
  { name: "model", label: "Model", type: "text" },
  { name: "year", label: "Year", type: "number" },
  { name: "grossWeightKg", label: "Gross weight (kg)", type: "number" },
  { name: "requiredLicenceClass", label: "Required licence class", type: "text" },
  { name: "leaseEndDate", label: "Lease end date", type: "date" },
  { name: "odometerKm", label: "Odometer (km)", type: "number" },
  { name: "depotId", label: "Depot ID", type: "text" },
  { name: "serviceIntervalKm", label: "Service interval (km)", type: "number" },
  { name: "serviceIntervalMonths", label: "Service interval (months)", type: "number" },
  { name: "lastTachoDownloadAt", label: "Last tacho download", type: "date" },
];
function initialValue(vehicle?: VehicleDetail): FormValues {
  return {
    plate: vehicle?.plate ?? "",
    vin: vehicle?.vin ?? "",
    make: vehicle?.make ?? "",
    model: vehicle?.model ?? "",
    year: vehicle ? String(vehicle.year) : "",
    grossWeightKg: vehicle ? String(vehicle.grossWeightKg) : "",
    requiredLicenceClass: vehicle?.requiredLicenceClass ?? "",
    leaseEndDate: vehicle?.leaseEndDate ?? "",
    odometerKm: vehicle?.odometerKm == null ? "" : String(vehicle.odometerKm),
    depotId: vehicle?.depotId ?? "",
    serviceIntervalKm: vehicle?.serviceIntervalKm == null ? "" : String(vehicle.serviceIntervalKm),
    serviceIntervalMonths:
      vehicle?.serviceIntervalMonths == null ? "" : String(vehicle.serviceIntervalMonths),
    lastTachoDownloadAt: vehicle?.lastTachoDownloadAt ?? "",
  };
}
const nullableString = (value: string): string | null => (value.trim() ? value : null);
const nullableNumber = (value: string): number | null => (value.trim() ? Number(value) : null);
function parseVehicleKind(value: string, fallback: VehicleKind): VehicleKind {
  if (value === "truck" || value === "trailer") return value;
  return fallback;
}
function parseOwnership(value: string, fallback: VehicleOwnership): VehicleOwnership {
  if (value === "owned" || value === "leased" || value === "rented_in") return value;
  return fallback;
}
function parseTrailerType(value: string): TrailerType | null {
  if (
    value === "curtainsider" ||
    value === "box" ||
    value === "reefer" ||
    value === "flatbed" ||
    value === "tanker"
  )
    return value;
  return null;
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
  const [values, setValues] = useState<FormValues>(() => initialValue(vehicle));
  const [kind, setKind] = useState<VehicleKind>(vehicle?.kind ?? "truck");
  const [ownership, setOwnership] = useState<VehicleOwnership>(vehicle?.ownership ?? "owned");
  const [trailerType, setTrailerType] = useState<TrailerType | null>(vehicle?.trailerType ?? null);
  const [reason, setReason] = useState(vehicle ? "" : "Added in Fleet UI");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [conflict, setConflict] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const isEdit = Boolean(vehicle);
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setErrors({});
    setFailure(null);
    setConflict(false);
    if (!reason.trim()) {
      setErrors({ reason: "Reason is required." });
      return;
    }
    const input: VehicleInput = {
      kind,
      plate: nullableString(values.plate),
      vin: nullableString(values.vin),
      make: nullableString(values.make),
      model: nullableString(values.model),
      year: Number(values.year),
      grossWeightKg: Number(values.grossWeightKg),
      requiredLicenceClass: nullableString(values.requiredLicenceClass),
      ownership,
      leaseEndDate: nullableString(values.leaseEndDate),
      trailerType: kind === "trailer" ? trailerType : null,
      odometerKm: kind === "truck" ? nullableNumber(values.odometerKm) : null,
      depotId: nullableString(values.depotId),
      serviceIntervalKm: kind === "truck" ? nullableNumber(values.serviceIntervalKm) : null,
      serviceIntervalMonths: kind === "truck" ? nullableNumber(values.serviceIntervalMonths) : null,
      lastTachoDownloadAt: kind === "truck" ? nullableString(values.lastTachoDownloadAt) : null,
      reason: nullableString(reason),
    };
    setSaving(true);
    try {
      const saved = vehicle
        ? await fleetApi.update(vehicle.id, { ...input, version: vehicle.version })
        : await fleetApi.create(input);
      onSaved(saved);
    } catch (error) {
      if (!(error instanceof AuthRequestError)) {
        setFailure("Vehicle could not be saved. Check the connection and try again.");
      } else {
        const problem: ApiProblem = error.problem;
        if (error.status === 409 && problem.code === "version_conflict") setConflict(true);
        if (error.status === 400) {
          const nextErrors: Record<string, string> = {};
          for (const [name, messages] of Object.entries(problem.errors ?? {}))
            nextErrors[name] = Array.isArray(messages) ? messages[0] : messages;
          for (const field of problem.fields ?? []) nextErrors[field] ??= "This field is invalid.";
          if (Object.keys(nextErrors).length) setErrors(nextErrors);
          else setFailure("Vehicle could not be saved. Review the form and try again.");
        } else if (error.status !== 409 || problem.code !== "version_conflict")
          setFailure("Vehicle could not be saved. Check the connection and try again.");
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
      {failure && (
        <Banner kind="error" label="Could not save vehicle">
          {failure}
        </Banner>
      )}
      {conflict && (
        <Banner kind="error" label="Version conflict">
          This vehicle changed elsewhere. Review your input and try again.
        </Banner>
      )}
      <form className="fleet-form" onSubmit={submit}>
        <label>
          Kind
          <select
            value={kind}
            onChange={(event) => setKind(parseVehicleKind(event.target.value, kind))}
            aria-invalid={Boolean(errors.kind)}
          >
            <option value="truck">{en.fleet.kind.truck}</option>
            <option value="trailer">{en.fleet.kind.trailer}</option>
          </select>
          {errors.kind && <span className="field-error">{errors.kind}</span>}
        </label>
        <label>
          Ownership
          <select
            value={ownership}
            onChange={(event) => setOwnership(parseOwnership(event.target.value, ownership))}
            aria-invalid={Boolean(errors.ownership)}
          >
            <option value="owned">{en.fleet.ownership.owned}</option>
            <option value="leased">{en.fleet.ownership.leased}</option>
            <option value="rented_in">{en.fleet.ownership.rented_in}</option>
          </select>
          {errors.ownership && <span className="field-error">{errors.ownership}</span>}
        </label>
        {kind === "trailer" && (
          <label>
            Trailer type
            <select
              value={trailerType ?? ""}
              onChange={(event) => setTrailerType(parseTrailerType(event.target.value))}
              aria-invalid={Boolean(errors.trailerType)}
            >
              <option value="">Select trailer type</option>
              {Object.entries(en.fleet.trailerType).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
            {errors.trailerType && <span className="field-error">{errors.trailerType}</span>}
          </label>
        )}
        {fields.map((field) => (
          <label key={field.name}>
            {field.label}
            <input
              type={field.type}
              value={values[field.name]}
              onChange={(event) => setValues((current) => ({ ...current, [field.name]: event.target.value }))}
              aria-invalid={Boolean(errors[field.name])}
            />
            {errors[field.name] && <span className="field-error">{errors[field.name]}</span>}
          </label>
        ))}
        <label>
          Reason
          <textarea
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            aria-invalid={Boolean(errors.reason)}
          />
          {errors.reason && <span className="field-error">{errors.reason}</span>}
        </label>
        <button className="ui-button primary" type="submit" disabled={saving}>
          {saving ? "Saving…" : isEdit ? "Save changes" : "Add vehicle"}
        </button>
      </form>
    </Drawer>
  );
}
