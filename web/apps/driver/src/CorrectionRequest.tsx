import { useId, useState, type FormEvent } from "react";
import type { components } from "@fleetops/api-client";
import type { DriverShiftState } from "./useShifts";
import { companyInstant, wallTime } from "./shiftTime";

export function CorrectionStatus({ request }: { request: components["schemas"]["CorrectionView"] }) {
  return (
    <div className="driver-card">
      <strong>Correction {request.status}</strong>
      {request.declineReason && <p>Declined: {request.declineReason}</p>}
      {request.diffs.map((diff) => (
        <p key={diff.field}>
          {diff.field}: {diff.before ?? "None"} {"\u2192"} {diff.after ?? "None"}
        </p>
      ))}
    </div>
  );
}

export function CorrectionRequest({
  state,
  date,
  shiftId,
  onClose,
}: {
  state: DriverShiftState;
  date: string;
  shiftId?: string;
  onClose: () => void;
}) {
  const fieldId = useId();
  const [error, setError] = useState("");
  const zone = state.snapshot?.companyTimeZone ?? "Europe/Berlin";
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError("");
    const form = new FormData(event.currentTarget);
    try {
      const start = String(form.get("start") ?? "");
      const end = String(form.get("end") ?? "");
      const proposedStartAt = start ? companyInstant(start, zone, `${date}T00:00:00-14:00`) : null;
      const proposedEndAt = end ? companyInstant(end, zone, proposedStartAt ?? `${date}T00:00:00-14:00`) : null;
      if (
        (proposedStartAt && Date.parse(proposedStartAt) > Date.now()) ||
        (proposedEndAt && Date.parse(proposedEndAt) > Date.now()) ||
        (proposedStartAt && proposedEndAt && proposedEndAt <= proposedStartAt)
      )
        throw new Error("invalid_time");
      if (
        await state.record("correction.request", crypto.randomUUID(), 0, {
          shiftId: shiftId ?? null,
          date,
          reason: shiftId ? form.get("reason") : "missing_shift",
          proposedStartAt,
          proposedEndAt,
          note: String(form.get("note") ?? "").trim() || null,
        })
      )
        onClose();
    } catch {
      setError("Choose valid proposed times before now, with end after start, in company time.");
    }
  };
  return (
    <form className="driver-sheet" onSubmit={submit}>
      <h3>
        {shiftId ? "Request correction" : "Add forgotten day"} {date}
      </h3>
      {shiftId && (
        <>
          <label htmlFor={`${fieldId}-reason`}>Reason</label>
          <select id={`${fieldId}-reason`} name="reason" required>
            <option value="wrong_times">Wrong times</option>
            <option value="wrong_activity">Wrong activity</option>
            <option value="wrong_country">Wrong country</option>
            <option value="other">Other</option>
          </select>
        </>
      )}
      <p>
        Proposed times (optional) {"\u00b7"} {zone}
      </p>
      <label htmlFor={`${fieldId}-start`}>Proposed start</label>
      <input id={`${fieldId}-start`} name="start" type="datetime-local" max={wallTime(Date.now(), zone)} />
      <label htmlFor={`${fieldId}-end`}>Proposed end</label>
      <input id={`${fieldId}-end`} name="end" type="datetime-local" max={wallTime(Date.now(), zone)} />
      <label htmlFor={`${fieldId}-note`}>Note (optional)</label>
      <textarea id={`${fieldId}-note`} name="note" maxLength={1000} />
      {(error || state.error) && <p role="alert">{error || state.error}</p>}
      <button disabled={state.saving}>Send request</button>
      <button type="button" disabled={state.saving} onClick={onClose}>
        Cancel
      </button>
    </form>
  );
}
