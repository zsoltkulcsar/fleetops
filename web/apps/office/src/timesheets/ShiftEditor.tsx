import { useId, useState, type FormEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@fleetops/api-client";
import { useOnline } from "../shell/Shell";
import { checked, timesheetsApi, type Schema } from "./api";
import { instant, wallTime } from "./presentation";

export function ShiftEditor({
  detail,
  employeeId,
  date,
  zone,
  onSaved,
}: {
  detail?: Schema["ShiftDetail"];
  employeeId: string;
  date: string;
  zone: string;
  onSaved: () => void;
}) {
  const fieldId = useId();
  const shift = detail?.shift;
  const running = shift?.status === "running";
  const online = useOnline();
  const [employee, setEmployee] = useState(employeeId);
  const [search, setSearch] = useState("");
  const [vehicleSearch, setVehicleSearch] = useState("");
  const [input, setInput] = useState<Schema["OfficeShiftInput"]>(() =>
    shift
      ? {
          date: shift.date,
          startAt: shift.startAt,
          endAt: shift.endAt,
          truckId: shift.truckId,
          trailerId: shift.trailerId,
          odometerStartKm: shift.odometerStartKm,
          odometerEndKm: shift.odometerEndKm,
          activities: structuredClone(shift.activities),
          overnights: structuredClone(shift.overnights),
        }
      : {
          date,
          startAt: "",
          endAt: null,
          truckId: null,
          trailerId: null,
          odometerStartKm: null,
          odometerEndKm: null,
          activities: [],
          overnights: [],
        },
  );
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const people = useQuery({
    queryKey: ["timesheet-people", search, employeeId],
    queryFn: () =>
      apiClient
        .GET("/api/v1/people/lookup", {
          params: { query: { search, ...(employeeId ? { id: employeeId } : {}) } },
        })
        .then(checked),
    enabled: !detail,
  });
  const vehicles = useQuery({
    queryKey: ["timesheet-vehicles", vehicleSearch],
    queryFn: () =>
      apiClient.GET("/api/v1/fleet/vehicles", { params: { query: { search: vehicleSearch } } }).then(checked),
  });
  const setTime = (element: HTMLInputElement, update: (value: string) => void) => {
    try {
      update(instant(element.value, zone));
      element.setCustomValidity("");
      setError("");
    } catch {
      element.setCustomValidity("Choose valid, unambiguous times in company time.");
      setError("Choose valid, unambiguous times in company time.");
    }
  };
  const save = async (event: FormEvent) => {
    event.preventDefault();
    setError("");
    if (!reason.trim() || !input.startAt || (!running && !input.endAt) || !employee) {
      setError("Complete employee, start, end and reason.");
      return;
    }
    const activities = input.activities?.length
      ? input.activities
      : [
          {
            id: crypto.randomUUID(),
            type: "other_work" as const,
            startAt: input.startAt,
            endAt: input.endAt,
          },
        ];
    if (
      (input.endAt !== null && Date.parse(input.endAt) <= Date.parse(input.startAt)) ||
      activities.some(
        (activity, index) =>
          (!activity.endAt && !(running && index === activities.length - 1)) ||
          (activity.endAt !== null && Date.parse(activity.endAt) <= Date.parse(activity.startAt)) ||
          (index > 0 && Date.parse(activity.startAt) !== Date.parse(activities[index - 1].endAt!)),
      ) ||
      Date.parse(activities[0].startAt) !== Date.parse(input.startAt) ||
      (activities[activities.length - 1].endAt === null
        ? input.endAt !== null
        : input.endAt === null ||
          Date.parse(activities[activities.length - 1].endAt!) !== Date.parse(input.endAt))
    ) {
      setError("Activities must be contiguous, with no gaps or overlaps, inside the shift start and end.");
      return;
    }
    setBusy(true);
    try {
      const body = { ...input, activities };
      if (shift)
        await timesheetsApi.edit(shift.id, { version: shift.version, reason: reason.trim(), input: body });
      else await timesheetsApi.add({ employeeId: employee, reason: reason.trim(), input: body });
      onSaved();
    } catch {
      setError(
        "Shift could not be saved. Check date, times, activities, vehicles and nights. The shift may have changed; close and reopen it before trying again.",
      );
    } finally {
      setBusy(false);
    }
  };
  return (
    <form className="ts-detail" onSubmit={save}>
      <h3>{shift ? "Edit shift" : "Add shift"}</h3>
      <p>
        Times in {zone}.{" "}
        {shift?.status === "approved"
          ? "Saving returns this approved shift to awaiting approval."
          : "The driver will be told the changes and reason."}
      </p>
      {!detail && (
        <>
          <div className="ts-field">
            <label htmlFor={`${fieldId}-driver-search`}>Find driver</label>
            <input
              id={`${fieldId}-driver-search`}
              value={search}
              maxLength={200}
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>
          <div className="ts-field">
            <label htmlFor={`${fieldId}-driver`}>Driver</label>
            <select
              id={`${fieldId}-driver`}
              required
              value={employee}
              onChange={(event) => setEmployee(event.target.value)}
            >
              <option value="">Choose driver</option>
              {people.data?.items.map((person) => (
                <option value={person.id} key={person.id}>
                  {person.name}
                </option>
              ))}
            </select>
          </div>
          {people.isError && (
            <button type="button" className="ui-button" onClick={() => void people.refetch()}>
              Try again loading drivers
            </button>
          )}
        </>
      )}
      <div className="ts-field">
        <label htmlFor={`${fieldId}-date`}>Date</label>
        <input
          id={`${fieldId}-date`}
          type="date"
          required
          value={input.date}
          onChange={(event) => setInput({ ...input, date: event.target.value })}
        />
      </div>
      <div className="ts-field">
        <label htmlFor={`${fieldId}-start`}>
          Start {"\u00B7"} {zone}
        </label>
        <input
          id={`${fieldId}-start`}
          type="datetime-local"
          required
          defaultValue={input.startAt ? wallTime(input.startAt, zone) : ""}
          onChange={(event) =>
            setTime(event.currentTarget, (value) => setInput({ ...input, startAt: value }))
          }
        />
      </div>
      {!running && (
        <div className="ts-field">
          <label htmlFor={`${fieldId}-end`}>
            End {"\u00B7"} {zone}
          </label>
          <input
            id={`${fieldId}-end`}
            type="datetime-local"
            required
            defaultValue={input.endAt ? wallTime(input.endAt, zone) : ""}
            onChange={(event) =>
              setTime(event.currentTarget, (value) => setInput({ ...input, endAt: value }))
            }
          />
        </div>
      )}
      <div className="ts-field">
        <label htmlFor={`${fieldId}-vehicle-search`}>Find vehicle by plate</label>
        <input
          id={`${fieldId}-vehicle-search`}
          value={vehicleSearch}
          maxLength={200}
          onChange={(event) => setVehicleSearch(event.target.value)}
        />
      </div>
      {(["truckId", "trailerId"] as const).map((field) => (
        <div className="ts-field" key={field}>
          <label htmlFor={`${fieldId}-vehicle-${field}`}>{field === "truckId" ? "Truck" : "Trailer"}</label>
          <select
            id={`${fieldId}-vehicle-${field}`}
            value={input[field] ?? ""}
            onChange={(event) => setInput({ ...input, [field]: event.target.value || null })}
          >
            <option value="">None</option>
            {input[field] && !vehicles.data?.items.some((vehicle) => vehicle.id === input[field]) && (
              <option value={input[field]!}>
                Current {field === "truckId" ? (detail?.truckPlate ?? "truck") : "trailer"}
              </option>
            )}
            {vehicles.data?.items
              .filter((vehicle) => vehicle.kind === (field === "truckId" ? "truck" : "trailer"))
              .map((vehicle) => (
                <option key={vehicle.id} value={vehicle.id}>
                  {vehicle.plate}
                </option>
              ))}
          </select>
        </div>
      ))}
      {vehicles.isError && (
        <button type="button" className="ui-button" onClick={() => void vehicles.refetch()}>
          Try again loading vehicles
        </button>
      )}
      {(["odometerStartKm", "odometerEndKm"] as const).map((field) => (
        <div className="ts-field" key={field}>
          <label htmlFor={`${fieldId}-odometer-${field}`}>
            {field === "odometerStartKm" ? "Start odometer (km)" : "End odometer (km)"}
          </label>
          <input
            id={`${fieldId}-odometer-${field}`}
            type="number"
            min={0}
            step={1}
            value={input[field] ?? ""}
            onChange={(event) =>
              setInput({ ...input, [field]: event.target.value === "" ? null : Number(event.target.value) })
            }
          />
        </div>
      ))}
      <h3>Activities</h3>
      {!input.activities?.length && <p>A single Other work block covering the shift will be created.</p>}
      {input.activities?.map((activity, index) => (
        <fieldset key={activity.id}>
          <legend>Activity {index + 1}</legend>
          <div className="ts-field">
            <label htmlFor={`${fieldId}-activity-type-${activity.id}`}>Type</label>
            <select
              id={`${fieldId}-activity-type-${activity.id}`}
              value={activity.type}
              onChange={(event) =>
                setInput({
                  ...input,
                  activities: input.activities!.map((item) =>
                    item.id === activity.id
                      ? { ...item, type: event.target.value as Schema["ActivityType"] }
                      : item,
                  ),
                })
              }
            >
              {(["driving", "other_work", "availability", "break"] as const).map((type) => (
                <option key={type} value={type}>
                  {type.replaceAll("_", " ")}
                </option>
              ))}
            </select>
          </div>
          {(["startAt", "endAt"] as const).map((field) => (
            <div className="ts-field" key={field}>
              <label htmlFor={`${fieldId}-activity-time-${activity.id}-${field}`}>
                {field === "startAt" ? "Start" : "End"} {"\u00B7"} {zone}
              </label>
              <input
                id={`${fieldId}-activity-time-${activity.id}-${field}`}
                type="datetime-local"
                required={!(running && field === "endAt" && index === input.activities!.length - 1)}
                defaultValue={activity[field] ? wallTime(activity[field]!, zone) : ""}
                onChange={(event) => {
                  if (running && field === "endAt" && event.currentTarget.value === "") {
                    event.currentTarget.setCustomValidity("");
                    setInput({
                      ...input,
                      activities: input.activities!.map((item) =>
                        item.id === activity.id ? { ...item, endAt: null } : item,
                      ),
                    });
                    return;
                  }
                  setTime(event.currentTarget, (value) =>
                    setInput({
                      ...input,
                      activities: input.activities!.map((item) =>
                        item.id === activity.id ? { ...item, [field]: value } : item,
                      ),
                    }),
                  );
                }}
              />
            </div>
          ))}
          <button
            type="button"
            className="ui-button"
            onClick={() =>
              setInput({ ...input, activities: input.activities!.filter((item) => item.id !== activity.id) })
            }
          >
            Remove activity
          </button>
        </fieldset>
      ))}
      <button
        type="button"
        className="ui-button"
        disabled={!input.startAt || (!running && !input.endAt)}
        onClick={() =>
          setInput({
            ...input,
            activities: [
              ...(input.activities ?? []),
              {
                id: crypto.randomUUID(),
                type: "other_work",
                startAt: input.activities?.at(-1)?.endAt ?? input.startAt,
                endAt: input.endAt,
              },
            ],
          })
        }
      >
        Add activity
      </button>
      <h3>Nights away</h3>
      {input.overnights?.map((night) => (
        <fieldset key={night.id}>
          <legend>Night away</legend>
          <div className="ts-field">
            <label htmlFor={`${fieldId}-night-date-${night.id}`}>Night</label>
            <input
              id={`${fieldId}-night-date-${night.id}`}
              type="date"
              required
              value={night.night}
              onChange={(event) =>
                setInput({
                  ...input,
                  overnights: input.overnights!.map((item) =>
                    item.id === night.id ? { ...item, night: event.target.value } : item,
                  ),
                })
              }
            />
          </div>
          <div className="ts-field">
            <label htmlFor={`${fieldId}-night-country-${night.id}`}>Country code</label>
            <input
              id={`${fieldId}-night-country-${night.id}`}
              required
              pattern="[A-Z]{2}"
              maxLength={2}
              value={night.countryCode}
              onChange={(event) =>
                setInput({
                  ...input,
                  overnights: input.overnights!.map((item) =>
                    item.id === night.id ? { ...item, countryCode: event.target.value.toUpperCase() } : item,
                  ),
                })
              }
            />
          </div>
          <div className="ts-field">
            <label htmlFor={`${fieldId}-night-kind-${night.id}`}>Kind</label>
            <select
              id={`${fieldId}-night-kind-${night.id}`}
              value={night.kind}
              onChange={(event) =>
                setInput({
                  ...input,
                  overnights: input.overnights!.map((item) =>
                    item.id === night.id
                      ? { ...item, kind: event.target.value as Schema["OvernightKind"] }
                      : item,
                  ),
                })
              }
            >
              <option value="cab">Cab</option>
              <option value="hotel">Hotel</option>
            </select>
          </div>
          <button
            className="ui-button"
            type="button"
            onClick={() =>
              setInput({ ...input, overnights: input.overnights!.filter((item) => item.id !== night.id) })
            }
          >
            Remove night
          </button>
        </fieldset>
      ))}
      <button
        className="ui-button"
        type="button"
        onClick={() =>
          setInput({
            ...input,
            overnights: [
              ...(input.overnights ?? []),
              { id: crypto.randomUUID(), night: input.date, countryCode: "DE", kind: "cab" },
            ],
          })
        }
      >
        Add night
      </button>
      <div className="ts-field">
        <label htmlFor={`${fieldId}-reason`}>Reason</label>
        <textarea
          id={`${fieldId}-reason`}
          required
          maxLength={500}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
        />
      </div>
      {error && <p role="alert">{error}</p>}
      <button className="ui-button primary" disabled={busy || !online || !reason.trim()}>
        {busy ? "Saving\u2026" : "Save shift"}
      </button>
    </form>
  );
}
