import { useId, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { apiClient, type components } from "@fleetops/api-client";
import { Banner, Drawer } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";
import { fleetApi } from "../fleet/api";
import { scheduleApi } from "../schedule/api";
import { checked, peopleApi, PeopleError, type Person, type PersonFields } from "./api";
import { roleLabels } from "./labels";
import { peopleErrorText } from "./errors";

export function PersonForm({
  person,
  onClose,
  onSaved,
}: {
  person?: Person;
  onClose: () => void;
  onSaved: (result: components["schemas"]["PersonSaveResult"], patternFailed: boolean) => void;
}) {
  const prefix = useId();
  const session = useSession();
  const online = useOnline();
  const [fields, setFields] = useState<PersonFields>(
    person?.fields ?? {
      firstName: "",
      lastName: "",
      phone: "",
      personnelNumber: "",
      email: "",
      startDate: new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Berlin" }).format(new Date()),
      employmentType: "full_time",
      languagePref: "de",
      adrRequired: false,
      vacationEntitlementDays: null,
    },
  );
  const [roles, setRoles] = useState(person?.header.roles ?? ["driver"]);
  const [pattern, setPattern] = useState<components["schemas"]["CyclePattern"]>({
    onWeeks: 2,
    offWeeks: 1,
    anchorDate: fields.startDate,
    vehicleId: "",
  });
  const [withPattern, setWithPattern] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const [stepUp, setStepUp] = useState(false);
  const [code, setCode] = useState("");
  const trucks = useQuery({
    queryKey: ["people-trucks"],
    queryFn: () => fleetApi.list({ kind: "truck" }),
    enabled: !person && roles.includes("driver") && withPattern,
  });
  const field = (key: keyof PersonFields, label: string, type = "text", required = false) => (
    <div className="people-field">
      <label htmlFor={`${prefix}-${key}`}>{label}</label>
      <input
        id={`${prefix}-${key}`}
        name={key}
        type={type}
        required={required}
        maxLength={key === "email" ? 320 : 100}
        value={String(fields[key] ?? "")}
        readOnly={key === "phone" && Boolean(person)}
        aria-describedby={key === "phone" && person ? `${prefix}-phone-note` : undefined}
        onChange={(event) => setFields({ ...fields, [key]: event.target.value })}
      />
    </div>
  );
  async function save() {
    setPending(true);
    setError("");
    try {
      if (stepUp) {
        const result = await apiClient.POST("/api/v1/auth/step-up", { body: { code } });
        setCode("");
        if (!result.response.ok) {
          setError("Authenticator code could not be confirmed. Try again.");
          return;
        }
        setStepUp(false);
      }
      const result = person
        ? await peopleApi.edit(person.header.id, fields)
        : await peopleApi.create({ ...fields, roles });
      let patternFailed = false;
      if (!person && withPattern && roles.includes("driver")) {
        try {
          const current = await scheduleApi.pattern(result.id);
          await scheduleApi.savePattern(result.id, { version: current.version, pattern });
        } catch {
          patternFailed = true;
        }
      }
      onSaved(result, patternFailed);
    } catch (failure) {
      if (failure instanceof PeopleError && failure.code === "step_up_required") {
        setStepUp(true);
        setError("Enter your authenticator code to add an owner.");
      } else setError(peopleErrorText(failure));
    } finally {
      setPending(false);
    }
  }
  return (
    <Drawer
      open
      title={person ? "Edit person" : "Add person"}
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
        <fieldset disabled={pending || !online}>
          <legend>Personal details</legend>
          {field("firstName", "First name", "text", true)}
          {field("lastName", "Last name", "text", true)}
          {field("phone", "Phone (international format)", "tel", true)}
          {person && (
            <p id={`${prefix}-phone-note`}>Use Change number on the Access tab to update the phone.</p>
          )}
          {field("personnelNumber", "Personnel number", "text", roles.includes("driver"))}
          {field(
            "email",
            "Email",
            "email",
            roles.some((role) => role !== "driver"),
          )}
          <div className="people-field">
            <label htmlFor={`${prefix}-depot`}>Depot</label>
            <input id={`${prefix}-depot`} value="Demo" readOnly />
            <p className="ui-muted">Assigned automatically.</p>
          </div>
        </fieldset>
        {!person && (
          <fieldset disabled={pending || !online}>
            <legend>Roles (choose at least one)</legend>
            {Object.entries(roleLabels)
              .filter(([role]) => role !== "owner" || session.data?.roles.includes("owner"))
              .map(([role, label]) => (
                <div className="people-check" key={role}>
                  <input
                    id={`${prefix}-role-${role}`}
                    type="checkbox"
                    checked={roles.includes(role)}
                    onChange={(event) =>
                      setRoles(
                        event.target.checked ? [...roles, role] : roles.filter((current) => current !== role),
                      )
                    }
                  />
                  <label htmlFor={`${prefix}-role-${role}`}>{label}</label>
                </div>
              ))}
          </fieldset>
        )}
        <fieldset disabled={pending || !online}>
          <legend>Employment</legend>
          {field("startDate", "Start date", "date", true)}
          <div className="people-field">
            <label htmlFor={`${prefix}-employment`}>Employment type</label>
            <select
              id={`${prefix}-employment`}
              required
              value={fields.employmentType ?? ""}
              onChange={(event) => setFields({ ...fields, employmentType: event.target.value })}
            >
              <option value="full_time">Full time</option>
              <option value="part_time">Part time</option>
              <option value="minijob">Minijob</option>
              <option value="other">Other</option>
            </select>
          </div>
          <div className="people-field">
            <label htmlFor={`${prefix}-language`}>Language</label>
            <select
              id={`${prefix}-language`}
              value={fields.languagePref ?? "de"}
              onChange={(event) => setFields({ ...fields, languagePref: event.target.value })}
            >
              <option value="de">German</option>
              <option value="en">English</option>
            </select>
          </div>
          <div className="people-field">
            <label htmlFor={`${prefix}-vacation`}>Vacation entitlement (days per year)</label>
            <input
              id={`${prefix}-vacation`}
              type="number"
              min="0"
              max="366"
              step="0.5"
              value={fields.vacationEntitlementDays ?? ""}
              onChange={(event) =>
                setFields({
                  ...fields,
                  vacationEntitlementDays: event.target.value === "" ? null : Number(event.target.value),
                })
              }
            />
          </div>
          {roles.includes("driver") && (
            <div className="people-check">
              <input
                id={`${prefix}-adr`}
                type="checkbox"
                checked={fields.adrRequired}
                onChange={(event) => setFields({ ...fields, adrRequired: event.target.checked })}
              />
              <label htmlFor={`${prefix}-adr`}>ADR certificate required</label>
            </div>
          )}
        </fieldset>
        {!person && roles.includes("driver") && (
          <fieldset disabled={pending || !online}>
            <legend>Cycle pattern and truck</legend>
            <div className="people-check">
              <input
                id={`${prefix}-pattern`}
                type="checkbox"
                checked={withPattern}
                onChange={(event) => setWithPattern(event.target.checked)}
              />
              <label htmlFor={`${prefix}-pattern`}>Set up cycle pattern</label>
            </div>
            {withPattern && (
              <>
                <label htmlFor={`${prefix}-on`}>Weeks on</label>
                <input
                  id={`${prefix}-on`}
                  type="number"
                  min="1"
                  max="52"
                  required
                  value={pattern.onWeeks}
                  onChange={(event) => setPattern({ ...pattern, onWeeks: Number(event.target.value) })}
                />
                <label htmlFor={`${prefix}-off`}>Weeks off</label>
                <input
                  id={`${prefix}-off`}
                  type="number"
                  min="1"
                  max="52"
                  required
                  value={pattern.offWeeks}
                  onChange={(event) => setPattern({ ...pattern, offWeeks: Number(event.target.value) })}
                />
                <label htmlFor={`${prefix}-anchor`}>Cycle start date</label>
                <input
                  id={`${prefix}-anchor`}
                  type="date"
                  required
                  value={pattern.anchorDate}
                  onChange={(event) => setPattern({ ...pattern, anchorDate: event.target.value })}
                />
                <label htmlFor={`${prefix}-truck`}>Truck</label>
                <select
                  id={`${prefix}-truck`}
                  required
                  value={pattern.vehicleId}
                  onChange={(event) => setPattern({ ...pattern, vehicleId: event.target.value })}
                >
                  <option value="">Choose a truck</option>
                  {trucks.data?.items.map((truck) => (
                    <option key={truck.id} value={truck.id}>
                      {truck.plate}
                    </option>
                  ))}
                </select>
                {trucks.isError && (
                  <p role="alert">
                    Trucks could not be loaded.{" "}
                    <button type="button" className="ui-button" onClick={() => void trucks.refetch()}>
                      Try again
                    </button>
                  </p>
                )}
              </>
            )}
          </fieldset>
        )}
        {stepUp && (
          <div className="people-field">
            <label htmlFor={`${prefix}-code`}>Enter your authenticator code</label>
            <input
              id={`${prefix}-code`}
              type="text"
              inputMode="numeric"
              autoComplete="one-time-code"
              required
              value={code}
              onChange={(event) => setCode(event.target.value)}
            />
          </div>
        )}
        {error && (
          <Banner kind="error" label="Check person details">
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
            disabled={pending || !online || roles.length === 0}
          >
            {pending ? "Saving\u2026" : "Save person"}
          </button>
        </div>
      </form>
    </Drawer>
  );
}
