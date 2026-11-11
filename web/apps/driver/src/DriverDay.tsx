import { useEffect, useState, type FormEvent } from "react";
import { Drawer } from "@fleetops/ui";
import { BriefcaseBusiness, CirclePause, Coffee, Truck } from "lucide-react";
import type { DriverShiftState } from "./useShifts";
import type { DriverSnapshot } from "./storage/snapshot";
import { companyDate, shiftHours, resolveSuggestedTruck } from "./storage/shiftProjection";
import { companyInstant, displayTime, wallTime } from "./shiftTime";
import { DriverReleaseNotice } from "./DriverReleaseNotice";
import { absenceLabels } from "./planningLabels";

const activities = [
  { type: "driving", label: "Driving", icon: Truck },
  { type: "other_work", label: "Other work", icon: BriefcaseBusiness },
  { type: "availability", label: "Availability", icon: CirclePause },
  { type: "break", label: "Break", icon: Coffee },
] as const;

export function DriverDay({
  state,
  driver,
  name,
  officePhone,
  employeeStatus,
}: {
  state: DriverShiftState;
  driver?: DriverSnapshot;
  name: string;
  officePhone: string;
  employeeStatus?: string;
}) {
  const [sheet, setSheet] = useState<"start" | "end">();
  const [truckId, setTruckId] = useState("");
  const [trailerId, setTrailerId] = useState("");
  const [night, setNight] = useState<"cab" | "hotel">();
  const [country, setCountry] = useState("DE");
  const [finishedCycle, setFinishedCycle] = useState<boolean | null>(null);
  const [now, setNow] = useState(Date.now());
  const [recentSwitch, setRecentSwitch] = useState<number>();
  const [formError, setFormError] = useState("");
  const running = state.shifts.find((shift) => shift.status === "running");
  const zone = state.snapshot?.companyTimeZone ?? "Europe/Berlin";
  const today = companyDate(new Date(now).toISOString(), zone);
  const absenceToday = driver?.absences?.find(
    (absence) => absence.status === "approved" && absence.from <= today && absence.to >= today,
  );
  const oldShift = !!running && running.date < companyDate(new Date(now).toISOString(), zone);
  const cycleEnd =
    !!running &&
    driver?.cycles.some(
      (cycle) => cycle.from <= running.date && (cycle.to === running.date || cycle.status === "overrun"),
    );
  const suspended =
    employeeStatus === "suspended" ||
    employeeStatus === "left" ||
    driver?.readinessCauses.some((cause) => cause.code === "suspended" || cause.code === "left");
  const onboarding = employeeStatus === "onboarding";
  const current = running?.activities.find((activity) => !activity.endAt);
  const selectedTruck = state.snapshot?.trucks.find((truck) => truck.id === truckId);
  const selectedTrailer = state.snapshot?.trucks.find((truck) => truck.id === trailerId);
  const blocked =
    running &&
    state.commands.some((command) => command.aggregateId === running.id && command.state !== "waiting");
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);
  const start = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    if (absenceToday && form.get("absence-confirmed") !== "on") return;
    const odometer = String(form.get("odometer") ?? "");
    if (
      await state.record("shift.start", crypto.randomUUID(), 0, {
        truckId: truckId || null,
        trailerId: trailerId || null,
        workWithoutVehicle: !truckId,
        odometerKm: odometer ? Number(odometer) : null,
        departureCheckOk: form.get("departure") === "on" ? true : null,
        startedOffline: !navigator.onLine,
      })
    )
      setSheet(undefined);
  };
  const end = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!running) return;
    const form = new FormData(event.currentTarget);
    setFormError("");
    try {
      const endAt = oldShift
        ? companyInstant(String(form.get("endAt")), zone, running.startAt)
        : new Date().toISOString();
      const odometer = String(form.get("odometer") ?? "");
      const actions = [
        {
          type: "shift.end",
          aggregateId: running.id,
          baseVersion: running.version,
          payload: { endAt, odometerKm: odometer ? Number(odometer) : null, finishedCycle } as unknown,
        },
      ];
      if (night)
        actions.push({
          type: "shift.night",
          aggregateId: running.id,
          baseVersion: running.version + 1,
          payload: { night: running.date, countryCode: country, kind: night },
        });
      if (!(await state.recordMany(actions))) return;
      setSheet(undefined);
      setNight(undefined);
    } catch (failure) {
      setFormError(
        failure instanceof Error && failure.message === "ambiguous_time"
          ? "That time occurs twice when the clocks change. Ask the Office to record the exact end time."
          : "Choose an end time after the shift started and before now, in company time.",
      );
    }
  };
  return (
    <section className="driver-screen">
      <h1>Shift</h1>
      <p className="driver-greeting">Hello, {name.trim().split(/\s+/)[0]}.</p>
      <p className="driver-sync">
        Company time {"\u00b7"} {zone}
        {!navigator.onLine ? " \u00b7 Offline" : ""}
      </p>
      {state.loading && <p role="status">Loading saved shifts{"\u2026"}</p>}
      {state.error && (
        <div className="driver-error" role="alert">
          <p>{state.error}</p>
          <button onClick={() => void state.sync()}>Retry</button>
        </div>
      )}
      {(suspended || onboarding) && (
        <p className="driver-error" role="alert">
          {onboarding ? "Not active yet. Contact HR" : "Account suspended. Contact HR"}
          <a className="driver-button" href={`tel:${officePhone}`}>
            Call HR
          </a>
        </p>
      )}
      {blocked && (
        <p className="driver-error" role="alert">
          This shift needs Office review. Your saved changes remain visible in sync status.
        </p>
      )}
      {running ? (
        <>
          <p className="driver-badge">
            Running {"\u00b7"} started {displayTime(running.startAt, zone)}
          </p>
          <div className="driver-shift-hours">
            {shiftHours(running, now).toFixed(2)}
            <span>hours excluding breaks</span>
          </div>
          <h2>{activities.find((activity) => activity.type === current?.type)?.label ?? "Other work"}</h2>
          {oldShift ? (
            <p className="driver-callout">
              Yesterday{"\u2019"}s shift is still running. Set its end time before starting today.
            </p>
          ) : (
            <div className="driver-activity-grid" aria-label="Activity">
              {activities.map(({ type, label, icon: Icon }) => (
                <button
                  key={type}
                  aria-pressed={current?.type === type}
                  disabled={state.saving || blocked || (type === "driving" && !running.truckId)}
                  onClick={async () => {
                    if (type === current?.type) return;
                    if (await state.record("shift.switch", running.id, running.version, { type }))
                      setRecentSwitch(Date.now());
                  }}
                >
                  <Icon aria-hidden size={26} />
                  {label}
                </button>
              ))}
            </div>
          )}
          {!running.truckId && (
            <p className="driver-sync">Working without a vehicle. Driving is unavailable.</p>
          )}
          {recentSwitch && now - recentSwitch <= 5000 && (
            <div className="driver-backdate" aria-label="Backdate activity switch">
              {[5, 15, 30].map((minutes) => (
                <button
                  key={minutes}
                  disabled={state.saving}
                  onClick={async () => {
                    const previous = running.activities.at(-2);
                    if (
                      !current ||
                      !previous ||
                      Date.parse(current.startAt) - minutes * 60000 < Date.parse(previous.startAt)
                    ) {
                      setFormError(
                        "That adjustment would cross the previous activity. Edit the times from Week instead.",
                      );
                      return;
                    }
                    if (await state.record("shift.backdate", running.id, running.version, { minutes }))
                      setRecentSwitch(undefined);
                  }}
                >
                  {"\u2212"}
                  {minutes} min
                </button>
              ))}
            </div>
          )}
          <div className="driver-actions">
            <button
              className="primary"
              disabled={state.saving || blocked}
              onClick={() => {
                setFormError("");
                setFinishedCycle(null);
                setCountry(state.shifts.flatMap((shift) => shift.overnights).at(-1)?.countryCode ?? "DE");
                setSheet("end");
              }}
            >
              End shift
            </button>
          </div>
        </>
      ) : (
        <>
          <p>No shift is running.</p>
          {absenceToday && (
            <p className="driver-callout">
              {absenceLabels[absenceToday.type]} until{" "}
              {new Intl.DateTimeFormat("de-DE", { timeZone: "UTC" }).format(
                new Date(`${absenceToday.to}T12:00:00Z`),
              )}
              . Confirm before starting work.
            </p>
          )}
          {driver && !driver.canDrive && !suspended && !onboarding && (
            <div className="driver-callout">
              <p>Work without a vehicle is available. Call HR about your driving documents.</p>
              <a className="driver-button" href={`tel:${officePhone}`}>
                Call HR
              </a>
            </div>
          )}
          {!suspended && !onboarding && (
            <div className="driver-actions">
              <button
                className="primary"
                disabled={state.loading || state.saving || !state.snapshot || !driver || suspended}
                onClick={() => {
                  const suggestedId = state.snapshot
                    ? resolveSuggestedTruck(
                        state.snapshot,
                        driver?.cycles ?? [],
                        companyDate(new Date().toISOString(), zone),
                      )
                    : null;
                  const suggested = state.snapshot?.trucks.find(
                    (truck) => truck.id === suggestedId && truck.usable,
                  );
                  setTruckId(driver?.canDrive ? (suggested?.id ?? "") : "");
                  setSheet("start");
                }}
              >
                Start shift
              </button>
            </div>
          )}
        </>
      )}
      {formError && (
        <p className="driver-error" role="alert">
          {formError}
        </p>
      )}
      {sheet === "start" && !suspended && !onboarding && (
        <Drawer
          open
          title="Start shift"
          closeLabel="Close start shift"
          onClose={() => {
            if (!state.saving) setSheet(undefined);
          }}
        >
          <form className="driver-sheet" onSubmit={start}>
            {absenceToday && (
              <label className="driver-callout">
                {`You're on ${absenceToday.type === "vacation" ? "vacation" : absenceLabels[absenceToday.type].toLowerCase()} today. Start anyway?`}
                <input type="checkbox" name="absence-confirmed" required /> Start anyway
              </label>
            )}
            <label htmlFor="start-truck">
              Truck
              <select
                id="start-truck"
                value={truckId}
                disabled={state.saving || !driver?.canDrive}
                onChange={(event) => {
                  setTruckId(event.target.value);
                  setTrailerId("");
                }}
              >
                <option value="">Work without a vehicle</option>
                {state.snapshot?.trucks
                  .filter((truck) => truck.kind === "truck")
                  .map((truck) => (
                    <option value={truck.id} key={truck.id} disabled={!truck.usable}>
                      {truck.plate}
                      {!truck.usable ? ` \u2014 ${truck.reason}` : ""}
                    </option>
                  ))}
              </select>
            </label>
            <DriverReleaseNotice release={selectedTruck?.release} />
            {selectedTruck?.licenceClassWarning && (
              <p className="driver-callout">
                Your recorded licence classes do not include this truck{"\u2019"}s class. Check with dispatch.
              </p>
            )}
            {truckId && (
              <>
                <label htmlFor="start-trailer">
                  Trailer (optional)
                  <select
                    id="start-trailer"
                    value={trailerId}
                    onChange={(event) => setTrailerId(event.target.value)}
                  >
                    <option value="">No trailer</option>
                    {state.snapshot?.trucks
                      .filter((truck) => truck.kind === "trailer")
                      .map((truck) => (
                        <option value={truck.id} key={truck.id}>
                          {truck.plate}
                        </option>
                      ))}
                  </select>
                </label>
                {selectedTrailer && !selectedTrailer.usable && (
                  <p className="driver-callout">
                    This trailer is unavailable. Check with dispatch before using it.
                  </p>
                )}
                <label htmlFor="start-departure">
                  <input id="start-departure" type="checkbox" name="departure" />
                  Departure check OK (optional)
                </label>
                <label htmlFor="start-odometer">
                  Odometer km (optional)
                  <input
                    id="start-odometer"
                    name="odometer"
                    inputMode="numeric"
                    type="number"
                    min={0}
                    step={1}
                  />
                </label>
              </>
            )}
            {state.error && (
              <p className="driver-error" role="alert">
                {state.error}
              </p>
            )}
            <button className="primary" disabled={state.saving}>
              {state.saving ? "Saving\u2026" : "Confirm start"}
            </button>
          </form>
        </Drawer>
      )}
      {sheet === "end" && running && (
        <Drawer
          open
          title="End shift"
          closeLabel="Close end shift"
          onClose={() => {
            if (!state.saving) setSheet(undefined);
          }}
        >
          <form className="driver-sheet" onSubmit={end}>
            {oldShift && (
              <label htmlFor="end-time">
                End time {"\u00b7"} {zone}
                <input
                  id="end-time"
                  type="datetime-local"
                  name="endAt"
                  required
                  defaultValue={wallTime(now, zone)}
                  max={wallTime(now, zone)}
                />
              </label>
            )}
            <label htmlFor="end-odometer">
              Odometer km (optional)
              <input id="end-odometer" name="odometer" inputMode="numeric" type="number" min={0} step={1} />
            </label>
            {cycleEnd && (
              <fieldset>
                <legend>Finished this Cycle?</legend>
                <div className="driver-backdate">
                  <button
                    type="button"
                    aria-pressed={finishedCycle === true}
                    onClick={() => setFinishedCycle(true)}
                  >
                    Finished this Cycle
                  </button>
                  <button
                    type="button"
                    aria-pressed={finishedCycle === false}
                    onClick={() => setFinishedCycle(false)}
                  >
                    Still on tour
                  </button>
                </div>
              </fieldset>
            )}
            <fieldset>
              <legend>Night away (optional)</legend>
              <div className="driver-backdate">
                <button
                  type="button"
                  aria-pressed={night === "cab"}
                  onClick={() => setNight(night === "cab" ? undefined : "cab")}
                >
                  Cab
                </button>
                <button
                  type="button"
                  aria-pressed={night === "hotel"}
                  onClick={() => setNight(night === "hotel" ? undefined : "hotel")}
                >
                  Hotel
                </button>
              </div>
            </fieldset>
            {night && (
              <label htmlFor="night-country">
                Country code
                <input
                  id="night-country"
                  value={country}
                  maxLength={2}
                  pattern="[A-Z]{2}"
                  onChange={(event) => setCountry(event.target.value.toUpperCase())}
                  required
                  autoComplete="country"
                  list="driver-countries"
                />
                <datalist id="driver-countries">
                  {["DE", "FR", "HU", "RO", "AT", "NL", "BE", "PL", "CZ", "IT", "ES", "SK", "SI", "CH"].map(
                    (code) => (
                      <option value={code} key={code} />
                    ),
                  )}
                </datalist>
              </label>
            )}
            {(formError || state.error) && (
              <p className="driver-error" role="alert">
                {formError || state.error}
              </p>
            )}
            <button className="primary" disabled={state.saving}>
              {state.saving ? "Saving\u2026" : "Confirm end"}
            </button>
          </form>
        </Drawer>
      )}
    </section>
  );
}
