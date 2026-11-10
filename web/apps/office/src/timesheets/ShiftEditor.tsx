import { useState, type FormEvent } from "react";
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
          <label>
            Find driver
            <input value={search} maxLength={200} onChange={(event) => setSearch(event.target.value)} />
          </label>
          <label>
            Driver
            <select required value={employee} onChange={(event) => setEmployee(event.target.value)}>
              <option value="">Choose driver</option>
              {people.data?.items.map((person) => (
                <option value={person.id} key={person.id}>
                  {person.name}
                </option>
              ))}
            </select>
          </label>
          {people.isError && (
            <button type="button" className="ui-button" onClick={() => void people.refetch()}>
              Try again loading drivers
            </button>
          )}
        </>
      )}
      <label>
        Date
        <input
          type="date"
          required
          value={input.date}
          onChange={(event) => setInput({ ...input, date: event.target.value })}
        />
      </label>
      <label>
        Start - {zone}
        <input
          type="datetime-local"
          required
          defaultValue={input.startAt ? wallTime(input.startAt, zone) : ""}
          onChange={(event) =>
            setTime(event.currentTarget, (value) => setInput({ ...input, startAt: value }))
          }
        />
      </label>
      {!running && (
        <label>
          End - {zone}
          <input
            type="datetime-local"
            required
            defaultValue={input.endAt ? wallTime(input.endAt, zone) : ""}
            onChange={(event) =>
              setTime(event.currentTarget, (value) => setInput({ ...input, endAt: value }))
            }
          />
        </label>
      )}
      <label>
        Find vehicle by plate
        <input
          value={vehicleSearch}
          maxLength={200}
          onChange={(event) => setVehicleSearch(event.target.value)}
        />
      </label>
      {(["truckId", "trailerId"] as const).map((field) => (
        <label key={field}>
          {field === "truckId" ? "Truck" : "Trailer"}
          <select
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
        </label>
      ))}
      {vehicles.isError && (
        <button type="button" className="ui-button" onClick={() => void vehicles.refetch()}>
          Try again loading vehicles
        </button>
      )}
      {(["odometerStartKm", "odometerEndKm"] as const).map((field) => (
        <label key={field}>
          {field === "odometerStartKm" ? "Start odometer (km)" : "End odometer (km)"}
          <input
            type="number"
            min={0}
            step={1}
            value={input[field] ?? ""}
            onChange={(event) =>
              setInput({ ...input, [field]: event.target.value === "" ? null : Number(event.target.value) })
            }
          />
        </label>
      ))}
      <h3>Activities</h3>
      {!input.activities?.length && <p>A single Other work block covering the shift will be created.</p>}
      {input.activities?.map((activity, index) => (
        <fieldset key={activity.id}>
          <legend>Activity {index + 1}</legend>
          <label>
            Type
            <select
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
          </label>
          {(["startAt", "endAt"] as const).map((field) => (
            <label key={field}>
              {field === "startAt" ? "Start" : "End"} - {zone}
              <input
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
            </label>
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
          <label>
            Night
            <input
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
          </label>
          <label>
            Country code
            <input
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
          </label>
          <label>
            Kind
            <select
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
          </label>
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
      <label>
        Reason
        <textarea
          required
          maxLength={500}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
        />
      </label>
      {error && <p role="alert">{error}</p>}
      <button className="ui-button primary" disabled={busy || !online || !reason.trim()}>
        {busy ? "Saving - " : "Save shift"}
      </button>
    </form>
  );
}
