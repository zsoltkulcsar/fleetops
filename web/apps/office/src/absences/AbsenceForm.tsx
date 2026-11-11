import { useNavigate } from "react-router";
import { useState, type FormEvent } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { components } from "@fleetops/api-client";
import { Banner, Drawer } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";
import { AbsenceError, absenceLabels, absencesApi, dayCounting, type AbsenceDetail } from "./api";
import { ShiftDrawer } from "../timesheets/ShiftDrawer";

export function AbsenceForm({
  employeeId,
  detail,
  onClose,
  onSaved,
}: {
  employeeId?: string;
  detail?: AbsenceDetail;
  onClose: () => void;
  onSaved: () => void;
}) {
  const navigate = useNavigate();
  const session = useSession();
  const hr = session.data?.roles.includes("hr") === true;
  const online = useOnline();
  const [personId, setPersonId] = useState(detail?.absence.employeeId ?? employeeId ?? "");
  const [type, setType] = useState<components["schemas"]["AbsenceType"]>(detail?.absence.type ?? "sick");
  const [from, setFrom] = useState(detail?.absence.from ?? "");
  const [to, setTo] = useState(detail?.absence.to ?? "");
  const [halfDay, setHalfDay] = useState<components["schemas"]["HalfDay"]>(detail?.absence.halfDay ?? null);
  const [note, setNote] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<AbsenceError>();
  const [shiftId, setShiftId] = useState<string>();
  const client = useQueryClient();
  const people = useQuery({
    queryKey: ["absence-people"],
    queryFn: () => absencesApi.people(),
    enabled: !employeeId && !detail,
  });
  const save = async (confirmOverlap = false) => {
    if (!online || busy || !personId || !from || to < from || (detail && !reason.trim())) return;
    setBusy(true);
    setError(undefined);
    try {
      if (detail)
        await absencesApi.edit(detail.absence.id, {
          from,
          to,
          halfDay: from === to ? halfDay : null,
          type,
          reason: reason.trim(),
          confirmOverlap,
        });
      else
        await absencesApi.record({
          employeeId: personId,
          from,
          to,
          halfDay: from === to ? halfDay : null,
          type,
          note: type === "sick" ? null : note.trim() || null,
          confirmOverlap,
        });
      await client.invalidateQueries({ queryKey: ["absences"] });
      await client.invalidateQueries({ queryKey: ["schedule"] });
      await client.invalidateQueries({ queryKey: ["attention"] });
      onSaved();
    } catch (failure) {
      setError(failure instanceof AbsenceError ? failure : new AbsenceError("request_failed", [], []));
    } finally {
      setBusy(false);
    }
  };
  const submit = (event: FormEvent) => {
    event.preventDefault();
    void save();
  };
  return (
    <>
      <Drawer
        open={!shiftId}
        title={detail ? "Edit absence" : "Record absence"}
        closeLabel="Close absence form"
        onClose={() => {
          if (!busy) onClose();
        }}
      >
        <form className="people-form" onSubmit={submit} onChange={() => setError(undefined)}>
          {!employeeId && !detail && (
            <label>
              Person
              <select required value={personId} onChange={(event) => setPersonId(event.target.value)}>
                <option value="">Choose person</option>
                {people.data?.map((person) => (
                  <option key={person.id} value={person.id}>
                    {person.name}
                  </option>
                ))}
              </select>
            </label>
          )}
          {people.isError && (
            <Banner
              kind="error"
              label="People unavailable"
              onRetry={() => void people.refetch()}
              retryLabel="Try again"
            >
              Could not load people.
            </Banner>
          )}
          <label>
            Type
            <select
              value={type}
              onChange={(event) => {
                const value = event.target.value;
                if (value in absenceLabels) setType(value as components["schemas"]["AbsenceType"]);
                setNote("");
              }}
            >
              {Object.entries(absenceLabels)
                .filter(([value]) => hr || value === "sick" || value === "other")
                .map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
            </select>
          </label>
          <label>
            From
            <input
              type="date"
              required
              value={from}
              onChange={(event) => {
                setFrom(event.target.value);
                setHalfDay(null);
              }}
            />
          </label>
          <label>
            To
            <input
              type="date"
              required
              min={from}
              value={to}
              onChange={(event) => {
                setTo(event.target.value);
                setHalfDay(null);
              }}
            />
          </label>
          <label>
            Half day
            <select
              value={halfDay ?? ""}
              disabled={!from || from !== to}
              onChange={(event) =>
                setHalfDay(event.target.value === "am" ? "am" : event.target.value === "pm" ? "pm" : null)
              }
            >
              <option value="">Full day</option>
              <option value="am">Morning</option>
              <option value="pm">Afternoon</option>
            </select>
          </label>
          {!detail && type !== "sick" && (
            <label>
              Note (optional)
              <textarea value={note} maxLength={2000} onChange={(event) => setNote(event.target.value)} />
            </label>
          )}
          {type === "sick" && <p>Sickness records contain dates only. Do not enter medical information.</p>}
          {detail && (
            <label>
              Reason required
              <textarea
                required
                maxLength={2000}
                value={reason}
                onChange={(event) => setReason(event.target.value)}
              />
            </label>
          )}
          <p className="ui-muted">{dayCounting}</p>
          {error && <AbsenceFailure error={error} onShift={setShiftId} />}
          {error?.code === "absence_overlaps_shifts" && (
            <button
              type="button"
              className="ui-button"
              disabled={busy || !online}
              onClick={() => void save(true)}
            >
              Save anyway
            </button>
          )}
          <button className="ui-button primary" disabled={busy || !online}>
            {busy ? "Saving\u2026" : "Save absence"}
          </button>
        </form>
      </Drawer>
      {shiftId && (
        <ShiftDrawer
          id={shiftId}
          zone="Europe/Berlin"
          onClose={() => setShiftId(undefined)}
          onSaved={() => {
            setShiftId(undefined);
            setError(undefined);
          }}
          onApprove={(shift) => navigate(`/timesheets?shiftId=${shift.id}`)}
        />
      )}
    </>
  );
}

export function AbsenceFailure({ error, onShift }: { error: AbsenceError; onShift: (id: string) => void }) {
  const messages: Record<string, string> = {
    running_shift: "A shift is running on the first day. End the shift before recording this absence.",
    absence_overlaps_shifts: "This absence overlaps existing shifts. No shifts will be deleted.",
    state_conflict: "This absence changed. Refresh and try again.",
    permission_denied: "Your role cannot perform this action.",
  };
  const shiftLabels: Record<string, string> = {
    running: "Running",
    draft: "Draft",
    submitted: "Awaiting approval",
    approved: "Approved",
    rejected: "Rejected",
    exported: "Exported",
  };
  return (
    <Banner kind="error" label="Absence could not be saved">
      <p>
        {messages[error.code] ??
          (error.fields.length
            ? `Check fields: ${error.fields.join(", ")}.`
            : "Check the fields and connection, then try again.")}
      </p>
      <ul>
        {error.shifts.map((shift, index) => {
          if (!shift || typeof shift !== "object") return null;
          const id: unknown = Reflect.get(shift, "id");
          const date: unknown = Reflect.get(shift, "date");
          const status: unknown = Reflect.get(shift, "status");
          return (
            <li key={typeof id === "string" ? id : index}>
              {typeof date === "string"
                ? new Date(`${date}T12:00:00Z`).toLocaleDateString("de-DE", { timeZone: "UTC" })
                : "Shift"}{" "}
              {typeof status === "string" ? (shiftLabels[status] ?? "Shift") : ""}
              {error.code === "running_shift" && typeof id === "string" && (
                <button type="button" className="ui-button" onClick={() => onShift(id)}>
                  End shift first
                </button>
              )}
            </li>
          );
        })}
      </ul>
    </Banner>
  );
}
