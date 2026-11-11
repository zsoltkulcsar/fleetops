import { CalendarDays, Truck } from "lucide-react";
import type { DriverSnapshot } from "./storage/snapshot";

export function driverDate(value: string) {
  return new Intl.DateTimeFormat("de-DE", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    timeZone: "Europe/Berlin",
  }).format(new Date(value.length === 10 ? `${value}T12:00:00Z` : value));
}
export function syncTime(value: string) {
  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    timeZone: "Europe/Berlin",
  }).format(new Date(value));
}
export function DriverSchedule({
  snapshot,
  online,
  busy,
  error,
  onRefresh,
  onTruck,
  shift = false,
  name,
  embedded = false,
}: {
  snapshot?: DriverSnapshot;
  online: boolean;
  busy: boolean;
  error: string;
  onRefresh: () => Promise<void>;
  onTruck: (id: string) => void;
  shift?: boolean;
  name?: string;
  embedded?: boolean;
}) {
  const today = new Intl.DateTimeFormat("en-CA", {
    timeZone: "Europe/Berlin",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
  const cycles =
    snapshot?.cycles
      .filter((cycle) => cycle.status !== "cancelled" && cycle.status !== "done")
      .sort((left, right) => left.from.localeCompare(right.from)) ?? [];
  const current = cycles.find(
    (cycle) => cycle.from <= today && (cycle.status === "overrun" || cycle.to >= today),
  );
  const next = cycles.find((cycle) => cycle.from > today);
  return (
    <section className="driver-screen driver-data-screen">
      {snapshot?.swaps
        ?.filter((swap) => !swap.to || swap.to >= today)
        .map((swap) => (
          <div className="driver-card" key={swap.id}>
            <h2>
              <Truck size={20} aria-hidden /> Swap
            </h2>
            <button onClick={() => onTruck(swap.vehicleId)}>{swap.vehiclePlate}</button>
            <p>
              {driverDate(swap.from)} to {swap.to ? driverDate(swap.to) : "Open ended"}
            </p>
          </div>
        ))}
      {!embedded && <h1>{shift ? "Shift" : "Week"}</h1>}
      {next && cycles.some((cycle) => cycle.status === "overrun") && (
        <p className="driver-callout">
          Your current Cycle has overrun. The next Cycle may need to change; contact dispatch.
        </p>
      )}
      {shift && <p className="driver-greeting">Hello, {name?.trim().split(/\s+/)[0]}.</p>}
      {snapshot && (
        <p className="driver-sync">
          {online ? "Last synced" : "Offline. As of"}{" "}
          <time dateTime={snapshot.generatedAt}>{syncTime(snapshot.generatedAt)}</time> · Europe/Berlin
        </p>
      )}
      {error && (
        <p className="driver-error" role="alert">
          {error}
        </p>
      )}
      {!snapshot && busy && <p role="status">Loading your schedule…</p>}
      {!snapshot && !busy && (
        <div className="driver-card">
          <CalendarDays size={28} aria-hidden />
          <h2>{online ? "No saved schedule yet" : "Connect to save your schedule"}</h2>
          <p>Your own dates and truck will be available here after a sync.</p>
        </div>
      )}
      {shift && snapshot && (
        <>
          {!snapshot.canDrive && (
            <div className="driver-callout" role="note">
              <strong>Driving is blocked</strong>
              <p>Open Me to check your documents. Ask the office if you need help.</p>
            </div>
          )}
          {current ? (
            <div className="driver-card">
              <span className="driver-label">Your truck</span>
              <strong className="driver-plate">{current.vehiclePlate}</strong>
              <p>
                {driverDate(current.from)} – {driverDate(current.to)}
              </p>
              <span className="driver-badge">
                {current.status === "overrun" ? "Tour overrun. Contact dispatch." : "On cycle"}
              </span>
              <button disabled={!online} onClick={() => onTruck(current.vehicleId)}>
                <Truck size={20} aria-hidden /> Open on this truck
              </button>
              {!online && <small>Truck details and photos need a connection.</small>}
            </div>
          ) : (
            <div className="driver-card">
              <h2>{next ? `Off until ${driverDate(next.from)}` : "No cycle planned"}</h2>
              <p>{next ? `Next truck: ${next.vehiclePlate}` : "Ask dispatch about your next tour."}</p>
            </div>
          )}
        </>
      )}
      {!shift && snapshot && (
        <div>
          <h2>Your schedule</h2>
          {cycles.length ? (
            <ol className="driver-cycle-list">
              {cycles.map((cycle) => (
                <li key={cycle.id}>
                  <CalendarDays size={24} aria-hidden />
                  <div>
                    <strong>
                      {driverDate(cycle.from)} – {driverDate(cycle.to)}
                    </strong>
                    <span>{cycle.vehiclePlate}</span>
                    <small>
                      {cycle.status}
                      {cycle.source === "manual" ? " · Changed by dispatch" : ""}
                    </small>
                  </div>
                  <button
                    aria-label={`Open truck ${cycle.vehiclePlate}`}
                    disabled={!online}
                    onClick={() => onTruck(cycle.vehicleId)}
                  >
                    <Truck size={22} aria-hidden />
                  </button>
                </li>
              ))}
            </ol>
          ) : (
            <p>No cycles planned. Ask dispatch for your next dates.</p>
          )}
        </div>
      )}
      <button disabled={!online || busy} onClick={() => void onRefresh()}>
        {busy ? "Refreshing…" : "Refresh schedule"}
      </button>
    </section>
  );
}
