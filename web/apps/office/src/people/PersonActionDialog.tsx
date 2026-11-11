import { useId, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Banner, Drawer } from "@fleetops/ui";
import { useOnline } from "../shell/Shell";
import { peopleApi, PeopleError, type Person } from "./api";
import { peopleErrorText } from "./errors";
import { documentLabel } from "./labels";

export type PersonAction =
  | "suspend"
  | "reactivate"
  | "leave"
  | "rehire"
  | "activate"
  | "phone"
  | "revoke"
  | "resetPin";
const titles: Record<PersonAction, string> = {
  suspend: "Suspend",
  reactivate: "Reactivate",
  leave: "Mark as left",
  rehire: "Rehire",
  activate: "Activate",
  phone: "Change number",
  revoke: "Revoke session",
  resetPin: "Reset PIN",
};

export function PersonActionDialog({
  person,
  action,
  sessionId,
  onClose,
  onSaved,
}: {
  person: Person;
  action: PersonAction;
  sessionId?: string;
  onClose: () => void;
  onSaved: () => void;
}) {
  const prefix = useId();
  const online = useOnline();
  const [reason, setReason] = useState("");
  const [phone, setPhone] = useState("");
  const [endDate, setEndDate] = useState(() =>
    new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Berlin" }).format(new Date()),
  );
  const [shiftEnd, setShiftEnd] = useState("");
  const [shiftReason, setShiftReason] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const [missing, setMissing] = useState<string[]>([]);
  const [activationState, setActivationState] = useState<"ready" | "warning" | "incomplete">("ready");
  const preview = useQuery({
    queryKey: ["leave-preview", person.header.id, endDate],
    queryFn: () => peopleApi.preview(person.header.id, endDate),
    enabled: action === "leave" && Boolean(endDate),
  });
  const needsReason = !["activate", "resetPin"].includes(action);
  async function save() {
    setPending(true);
    setError("");
    try {
      const id = person.header.id;
      if (needsReason && !reason.trim()) {
        setError("A reason is required.");
        return;
      }
      if (action === "activate") await peopleApi.activate(id, activationState === "warning");
      if (action === "suspend" || action === "reactivate" || action === "rehire")
        await peopleApi[action](id, { reason });
      if (action === "leave") {
        if (!preview.data || preview.isFetching || preview.isError) return;
        await peopleApi.leave(id, {
          endDate,
          reason,
          runningShiftEnd: preview.data.runningShiftId && shiftEnd ? new Date(shiftEnd).toISOString() : null,
          runningShiftReason: preview.data.runningShiftId ? shiftReason : null,
        });
      }
      if (action === "phone") await peopleApi.phone(id, { phone, reason });
      if (action === "revoke") {
        if (!sessionId) {
          setError("Choose a session to revoke.");
          return;
        }
        await peopleApi.revoke(id, sessionId, { reason });
      }
      if (action === "resetPin") await peopleApi.resetPin(id);
      onSaved();
    } catch (failure) {
      if (
        failure instanceof PeopleError &&
        ["activation_needs_warning", "activation_incomplete"].includes(failure.code)
      ) {
        setMissing(failure.types);
        setActivationState(failure.code === "activation_needs_warning" ? "warning" : "incomplete");
      } else setError(peopleErrorText(failure));
    } finally {
      setPending(false);
    }
  }
  return (
    <Drawer
      open
      title={`${titles[action]}: ${person.header.name}`}
      closeLabel="Close"
      onClose={() => {
        if (!pending) onClose();
      }}
    >
      <form
        className="people-form"
        onSubmit={(event) => {
          event.preventDefault();
          void save();
        }}
      >
        {action === "activate" && (
          <p>
            {activationState === "warning"
              ? "Starts blocked, non-driving."
              : activationState === "incomplete"
                ? "Activation is incomplete. Verify the missing documents before trying again."
                : "Activate this person after reviewing their onboarding checklist."}
          </p>
        )}
        {missing.length > 0 && (
          <div role="alert">
            <h3>Missing documents</h3>
            <ul>
              {missing.map((type) => (
                <li key={type}>{documentLabel(type)}</li>
              ))}
            </ul>
          </div>
        )}
        {action === "suspend" && (
          <p>The driver cannot start new shifts. Document uploads remain available.</p>
        )}
        {action === "rehire" && (
          <p>
            The person returns to active status. Previous sessions stay revoked. Use Resend enrolment on the
            Access tab.
          </p>
        )}
        {action === "resetPin" && (
          <p>The driver will choose a new PIN on their phone, including from the locked screen.</p>
        )}
        {action === "phone" && (
          <>
            <p>
              Active phone sessions will be revoked and the old number will be notified. Resend enrolment to
              the new number afterwards.
            </p>
            <label htmlFor={`${prefix}-phone`}>New phone (international format)</label>
            <input
              id={`${prefix}-phone`}
              type="tel"
              pattern="\+[1-9][0-9]{1,14}"
              required
              value={phone}
              onChange={(event) => setPhone(event.target.value)}
            />
          </>
        )}
        {action === "leave" && (
          <>
            <label htmlFor={`${prefix}-end`}>Employment end date</label>
            <input
              id={`${prefix}-end`}
              type="date"
              required
              value={endDate}
              onChange={(event) => setEndDate(event.target.value)}
            />
            {preview.isFetching && <p role="status">{"Loading leave preview\u2026"}</p>}
            {preview.isError && (
              <Banner
                kind="error"
                label="Leave preview could not be loaded"
                retryLabel="Try again"
                onRetry={() => void preview.refetch()}
              >
                Confirmation is unavailable until the preview loads.
              </Banner>
            )}
            {preview.data && !preview.isFetching && !preview.isError && (
              <section aria-label="Leave preview">
                <h3>Changes on confirmation</h3>
                <ul>
                  <li>{preview.data.sessionsToRevoke.length} sessions will be revoked.</li>
                  <li>
                    {preview.data.assignmentsToEnd.length} assignments will end;{" "}
                    {preview.data.assignmentsToRemove.length} will be removed.
                  </li>
                  <li>
                    {preview.data.cyclesToEnd.length} cycles will end; {preview.data.cyclesToRemove.length}{" "}
                    will be removed.
                  </li>
                  <li>{preview.data.alertsToResolve.length} alerts will resolve.</li>
                  <li>{preview.data.shiftsToApprove.length} shifts remain available for approval.</li>
                </ul>
                {preview.data.runningShiftId && (
                  <>
                    <p>A shift is running. Enter its end time and reason before confirming.</p>
                    <label htmlFor={`${prefix}-shift-end`}>Running shift end time (your local time)</label>
                    <input
                      id={`${prefix}-shift-end`}
                      type="datetime-local"
                      required
                      value={shiftEnd}
                      onChange={(event) => setShiftEnd(event.target.value)}
                    />
                    <label htmlFor={`${prefix}-shift-reason`}>Reason for ending the running shift</label>
                    <textarea
                      id={`${prefix}-shift-reason`}
                      required
                      value={shiftReason}
                      onChange={(event) => setShiftReason(event.target.value)}
                    />
                  </>
                )}
              </section>
            )}
          </>
        )}
        {needsReason && (
          <>
            <label htmlFor={`${prefix}-reason`}>Reason</label>
            <textarea
              id={`${prefix}-reason`}
              required
              value={reason}
              onChange={(event) => setReason(event.target.value)}
            />
          </>
        )}
        {error && (
          <Banner kind="error" label="Change could not be saved">
            {error}
          </Banner>
        )}
        <div className="people-actions">
          <button type="button" className="ui-button" disabled={pending} onClick={onClose}>
            Cancel
          </button>
          <button
            type="submit"
            className="ui-button primary"
            disabled={
              pending ||
              !online ||
              activationState === "incomplete" ||
              (action === "leave" && (!preview.data || preview.isFetching || preview.isError))
            }
          >
            {pending
              ? "Saving\u2026"
              : activationState === "warning"
                ? "Confirm activation with warning"
                : titles[action]}
          </button>
        </div>
      </form>
    </Drawer>
  );
}
