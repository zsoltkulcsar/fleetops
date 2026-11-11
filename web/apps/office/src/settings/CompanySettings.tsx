import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient, type components } from "@fleetops/api-client";
import { Banner, Panel } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { checked, PeopleError, peopleApi } from "../people/api";
import { documentLabel, personRoles, roleLabels } from "../people/labels";
import "./settings.css";

const companyFields = [
  ["company.max_shift_hours", "Maximum shift hours", 0, 24],
  ["alerts.escalation_minutes", "Escalation minutes", 1, 1440],
] as const;
const alertFields = [
  ["alerts.a03.blocked_hours", "A-03 blocked hours", 1, 168],
  ["alerts.a03.queue_days", "A-03 queue days", 1, 365],
  ["alerts.a08.days", "A-08 days", 1, 365],
  ["vehicles.lease_lead_days", "Lease lead days", 0, 365],
] as const;
const releaseTypes = ["hu_report", "sp_report", "tacho_calibration", "community_licence"];

export function CompanySettings() {
  const session = useSession();
  const owner = session.data?.roles.includes("owner") === true;
  const client = useQueryClient();
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const settings = useQuery({
    queryKey: ["company-settings"],
    queryFn: () => apiClient.GET("/api/v1/settings").then(checked),
  });
  async function perform(operation: () => Promise<components["schemas"]["SettingsView"]>) {
    setBusy(true);
    setMessage("");
    try {
      client.setQueryData(["company-settings"], await operation());
      setMessage("Settings saved.");
    } catch (error) {
      setMessage(
        error instanceof PeopleError && error.fields.length
          ? `Check fields: ${error.fields.join(", ")}`
          : "Settings could not be saved. Try again.",
      );
    } finally {
      setBusy(false);
    }
  }
  if (!settings.data)
    return (
      <Banner label="Settings">
        {settings.isError ? "Settings could not be loaded." : "Loading settings\u2026"}
        <button className="ui-button" onClick={() => void settings.refetch()}>
          Try again
        </button>
      </Banner>
    );
  const data = settings.data;
  function fields(entries: ReadonlyArray<readonly [string, string, number, number]>) {
    return entries.map(([key, label, min, max]) => (
      <div key={key}>
        <label htmlFor={key}>{label}</label>
        <input
          id={key}
          name={key}
          type="number"
          min={min}
          max={max}
          required
          defaultValue={data.values[key]}
        />
      </div>
    ));
  }
  return (
    <div className="page settings-sections">
      {message && <p role="status">{message}</p>}
      <form
        key={JSON.stringify(data.values) + data.releasableDocTypes.join()}
        onSubmit={(event) => {
          event.preventDefault();
          const form = new FormData(event.currentTarget);
          const values: components["schemas"]["SettingsView"]["values"] = {};
          for (const [key, value] of form.entries())
            if (key !== "release" && Number(value) !== data.values[key]) values[key] = Number(value);
          const releaseChanged =
            owner &&
            JSON.stringify(form.getAll("release").sort()) !==
              JSON.stringify([...data.releasableDocTypes].sort());
          const changes: components["schemas"]["UpdateSettingsRequest"] = {
            ...values,
            ...(releaseChanged ? { "company.releasable_doc_types": form.getAll("release").map(String) } : {}),
          };
          void perform(() => apiClient.PUT("/api/v1/settings", { body: changes }).then(checked));
        }}
      >
        {owner && (
          <>
            <Panel title="Company">
              {fields(companyFields)}
              <fieldset>
                <legend>Releasable document types</legend>
                {releaseTypes.map((type) => (
                  <div key={type}>
                    <input
                      id={`release-${type}`}
                      name="release"
                      type="checkbox"
                      value={type}
                      defaultChecked={data.releasableDocTypes.includes(type)}
                    />
                    <label htmlFor={`release-${type}`}>{documentLabel(type)}</label>
                  </div>
                ))}
              </fieldset>
            </Panel>
            <Panel title="Alerts">{fields(alertFields)}</Panel>
            <Panel title="Lead days per document type">
              {fields(
                Object.keys(data.values)
                  .filter((key) => key.startsWith("documents.lead_days."))
                  .map(
                    (key) => [key, documentLabel(key.slice("documents.lead_days.".length)), 0, 365] as const,
                  ),
              )}
            </Panel>
          </>
        )}
        <Panel title="Payroll">{fields([["company.payroll_cutoff_day", "Cutoff day", 1, 28]])}</Panel>
        <button className="ui-button primary" disabled={busy || !navigator.onLine}>
          Save settings
        </button>
      </form>
      <Panel title="Holidays">
        <p className="ui-muted">Check against your Bundesland (unverified)</p>
        <ul>
          {data.holidays.map((holiday) => (
            <li key={holiday.date}>
              {holiday.date} {holiday.name}{" "}
              <button
                className="ui-button"
                disabled={busy || !navigator.onLine}
                onClick={() =>
                  void perform(() =>
                    apiClient
                      .DELETE("/api/v1/settings/holidays/{date}", {
                        params: { path: { date: holiday.date } },
                      })
                      .then(checked),
                  )
                }
              >
                Remove
              </button>
            </li>
          ))}
        </ul>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            const form = new FormData(event.currentTarget);
            void perform(() =>
              apiClient
                .POST("/api/v1/settings/holidays", {
                  body: { date: String(form.get("date")), name: String(form.get("name")) },
                })
                .then(checked),
            );
          }}
        >
          <label htmlFor="holiday-date">Date</label>
          <input id="holiday-date" name="date" type="date" required />
          <label htmlFor="holiday-name">Name</label>
          <input id="holiday-name" name="name" maxLength={200} required />
          <button className="ui-button" disabled={busy || !navigator.onLine}>
            Add holiday
          </button>
        </form>
      </Panel>
      <OfficeUsers />
    </div>
  );
}

function OfficeUsers() {
  const [page, setPage] = useState(1);
  const people = useQuery({
    queryKey: ["office-users", page],
    queryFn: () => peopleApi.list({ page, pageSize: 50 }),
  });
  return (
    <Panel title="Users & roles">
      {people.isError && (
        <p role="alert">
          People could not be loaded.<button onClick={() => void people.refetch()}>Try again</button>
        </p>
      )}
      {people.data?.items
        .filter((person) => person.roles.some((role) => role !== "driver"))
        .map((person) => (
          <OfficeUser key={person.id} person={person} onSaved={() => void people.refetch()} />
        ))}
      <button className="ui-button" disabled={page === 1} onClick={() => setPage(page - 1)}>
        Previous
      </button>
      <span>Page {page}</span>
      <button
        className="ui-button"
        disabled={(people.data?.items.length ?? 0) < 50}
        onClick={() => setPage(page + 1)}
      >
        Next
      </button>
    </Panel>
  );
}

function OfficeUser({
  person,
  onSaved,
}: {
  person: components["schemas"]["PersonRow"];
  onSaved: () => void;
}) {
  const session = useSession();
  const owner = session.data?.roles.includes("owner") === true;
  const [editing, setEditing] = useState(false);
  const [roles, setRoles] = useState(person.roles);
  const [reason, setReason] = useState("");
  const [stepUp, setStepUp] = useState(false);
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  async function save() {
    setBusy(true);
    try {
      if (stepUp) {
        const result = await apiClient.POST("/api/v1/auth/step-up", {
          body: session.data?.stepUpMethod === "password" ? { password: code } : { code },
        });
        setCode("");
        if (!result.response.ok) {
          setMessage("Verification failed. Try again.");
          return;
        }
        setStepUp(false);
      }
      await apiClient
        .PUT("/api/v1/people/{id}/roles", { params: { path: { id: person.id } }, body: { roles, reason } })
        .then(checked);
      setEditing(false);
      setMessage("Roles saved.");
      onSaved();
    } catch (error) {
      if (error instanceof PeopleError && error.code === "step_up_required") {
        setStepUp(true);
        setMessage("Verify your identity to save roles.");
      } else
        setMessage(
          error instanceof PeopleError && error.code === "last_owner"
            ? "The last owner cannot be removed. Add another owner first."
            : "Roles could not be saved.",
        );
    } finally {
      setBusy(false);
    }
  }
  return (
    <article className="people-session">
      <h3>{person.name}</h3>
      <p>{personRoles(person.roles)}</p>
      <p role="status">{message}</p>
      <button className="ui-button" onClick={() => setEditing(!editing)}>
        Edit roles
      </button>
      <button
        className="ui-button"
        disabled={busy || !navigator.onLine}
        onClick={async () => {
          setBusy(true);
          try {
            await apiClient
              .POST("/api/v1/people/{id}/invite", { params: { path: { id: person.id } } })
              .then(checked);
            setMessage("Invitation sent.");
          } catch {
            setMessage("Invitation could not be sent.");
          } finally {
            setBusy(false);
          }
        }}
      >
        Send invite
      </button>
      {editing && (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            void save();
          }}
        >
          <fieldset>
            <legend>Roles for {person.name}</legend>
            {Object.entries(roleLabels).map(([role, label]) => (
              <div key={role}>
                <input
                  id={`${person.id}-${role}`}
                  type="checkbox"
                  disabled={role === "owner" && !owner}
                  checked={roles.includes(role)}
                  onChange={(event) =>
                    setRoles(
                      event.target.checked ? [...roles, role] : roles.filter((value) => value !== role),
                    )
                  }
                />
                <label htmlFor={`${person.id}-${role}`}>{label}</label>
              </div>
            ))}
          </fieldset>
          <label htmlFor={`reason-${person.id}`}>Reason</label>
          <input
            id={`reason-${person.id}`}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            required
            maxLength={500}
          />
          {stepUp && (
            <>
              <label htmlFor={`code-${person.id}`}>
                {session.data?.stepUpMethod === "password" ? "Password" : "Authenticator code"}
              </label>
              <input
                id={`code-${person.id}`}
                value={code}
                onChange={(event) => setCode(event.target.value)}
                type={session.data?.stepUpMethod === "password" ? "password" : "text"}
                autoComplete={
                  session.data?.stepUpMethod === "password" ? "current-password" : "one-time-code"
                }
                inputMode={session.data?.stepUpMethod === "password" ? "text" : "numeric"}
                required
              />
            </>
          )}
          <button className="ui-button" disabled={busy || !navigator.onLine}>
            Save roles
          </button>
        </form>
      )}
    </article>
  );
}
