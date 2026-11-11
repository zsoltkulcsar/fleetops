import { useEffect, useMemo, useRef, useState } from "react";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { Plus, Search } from "lucide-react";
import { Banner, EmptyState, Plate, StatusPill, Toast } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { en } from "../i18n/en";
import { fleetApi, type Vehicle } from "./api";
import { VehicleForm } from "./VehicleForm";
import "./fleet.css";
import { readinessApi, type VehicleReadiness } from "../readiness/api";
import { fleetCauseText } from "../readiness/labels";
import { FleetTabs } from "./FleetTabs";
import { VehicleRelease } from "./VehicleRelease";

type ReadinessVehicle = Vehicle & VehicleReadiness;
const statusPillFor: Record<ReadinessVehicle["shownStatus"], "usable" | "off-road"> = {
  active: "usable",
  off_road: "off-road",
  in_maintenance: "off-road",
  retired: "off-road",
};
const statusLabel: Record<ReadinessVehicle["shownStatus"], string> = {
  active: "Usable",
  off_road: "Off road",
  in_maintenance: "In workshop",
  retired: "Retired",
};

export function Fleet() {
  const heading = useRef<HTMLHeadingElement>(null);
  const session = useSession();
  const canEdit = Boolean(session.data?.roles.some((role) => role === "dispatcher" || role === "owner"));
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [kind, setKind] = useState<Vehicle["kind"] | "">("");
  const [includeRetired, setIncludeRetired] = useState(false);
  const [formOpen, setFormOpen] = useState(false);
  const [toast, setToast] = useState(false);
  useEffect(() => {
    document.title = `Fleet \u00b7 ${en.appName}`;
  }, []);
  useEffect(() => {
    const timer = window.setTimeout(() => setTerm(search.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [search]);
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target;
      const isTyping =
        target instanceof HTMLInputElement ||
        target instanceof HTMLTextAreaElement ||
        target instanceof HTMLSelectElement ||
        (target instanceof HTMLElement && target.isContentEditable);
      if (event.key !== "/" || isTyping) return;
      event.preventDefault();
      document.getElementById("fleet-search")?.focus();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, []);
  const query = useInfiniteQuery({
    queryKey: ["fleet", "vehicles", term, kind, includeRetired],
    initialPageParam: "",
    queryFn: ({ pageParam }) =>
      fleetApi.list({
        cursor: pageParam || undefined,
        search: term,
        kind: kind || undefined,
        includeRetired,
      }),
    getNextPageParam: (page) => page.nextCursor ?? undefined,
  });
  const summary = useQuery({ queryKey: ["readiness-summary"], queryFn: readinessApi.summary });
  const vehicles = useMemo(
    () => query.data?.pages.flatMap((page) => page.items as ReadinessVehicle[]) ?? [],
    [query.data],
  );
  return (
    <div className="page fleet-page directory-page">
      <div className="page-title">
        <div>
          <h1 ref={heading} tabIndex={-1}>
            Fleet
          </h1>
        </div>
        {canEdit && (
          <button className="ui-button primary" onClick={() => setFormOpen(true)}>
            <Plus size={17} aria-hidden /> Add vehicle
          </button>
        )}
      </div>
      <FleetTabs />
      {query.isError && (
        <Banner label="Error" retryLabel="Try again" onRetry={() => void query.refetch()}>
          Data could not be loaded. Check the connection and try again.
        </Banner>
      )}
      <section className="ui-panel directory-panel" aria-label="Vehicles">
        <header className="directory-toolbar">
          <div className="directory-heading">
            <h2>Vehicles</h2>
            {summary.data && (
              <span className="ui-muted">
                {summary.data.vehiclesUsable} of {summary.data.vehiclesTotal} usable
              </span>
            )}
          </div>
          <div className="fleet-filters">
            <div className="directory-chips" role="group" aria-label="Vehicle kind">
              {(
                [
                  ["", "All"],
                  ["truck", "Trucks"],
                  ["trailer", "Trailers"],
                ] as const
              ).map(([value, label]) => (
                <button
                  type="button"
                  key={value}
                  className="directory-chip"
                  aria-pressed={kind === value}
                  onClick={() => setKind(value)}
                >
                  {label}
                </button>
              ))}
            </div>
            <label className="search-field" htmlFor="fleet-search">
              <Search size={16} aria-hidden />
              <span className="visually-hidden">Search fleet</span>
              <input
                id="fleet-search"
                placeholder="Search by plate"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
              />
            </label>
            <label className="checkbox-field" htmlFor="fleet-include-retired">
              <input
                id="fleet-include-retired"
                type="checkbox"
                checked={includeRetired}
                onChange={(event) => setIncludeRetired(event.target.checked)}
              />
              {en.fleet.includeRetired}
            </label>
          </div>
        </header>
        {query.isLoading ? (
          <div className="fleet-loading">Loading vehicles{"\u2026"}</div>
        ) : !vehicles.length && !query.isError ? (
          <EmptyState
            heading="No vehicles yet."
            primary={canEdit ? { label: "Add vehicle", onClick: () => setFormOpen(true) } : undefined}
          >
            Add the first vehicle to start tracking your fleet.
          </EmptyState>
        ) : (
          <div className="table-wrap">
            <table>
              <caption className="visually-hidden">Fleet vehicles</caption>
              <thead>
                <tr>
                  <th>Plate</th>
                  <th>Vehicle</th>
                  <th>Status</th>
                  <th>Kind</th>
                  <th>Ownership</th>
                  <th>Year</th>
                </tr>
              </thead>
              <tbody>
                {vehicles.map((vehicle) => (
                  <tr
                    key={vehicle.id}
                    tabIndex={0}
                    onClick={() => {
                      window.location.href = `/fleet/${vehicle.id}`;
                    }}
                    onKeyDown={(event) => {
                      if (event.key === "Enter") window.location.href = `/fleet/${vehicle.id}`;
                    }}
                  >
                    <td>
                      <Plate>{vehicle.plate}</Plate>
                    </td>
                    <td>
                      {vehicle.make} {vehicle.model}
                    </td>
                    <td>
                      {(() => {
                        const shownStatus =
                          vehicle.shownStatus ?? (vehicle.status === "retired" ? "retired" : "active");
                        const notUsable = shownStatus === "active" && vehicle.usable === false;
                        return (
                          <>
                            <StatusPill
                              status={notUsable ? "off-road" : statusPillFor[shownStatus]}
                              label={notUsable ? "Not usable" : statusLabel[shownStatus]}
                            />
                            {vehicle.causes?.map((cause) => (
                              <div key={`${cause.code}-${cause.documentType}`}>{fleetCauseText(cause)}</div>
                            ))}
                            <VehicleRelease id={vehicle.id} />
                          </>
                        );
                      })()}
                    </td>
                    <td>{en.fleet.kind[vehicle.kind]}</td>
                    <td>{en.fleet.ownership[vehicle.ownership]}</td>
                    <td className="tabular-nums">{vehicle.year}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <footer className="directory-footer">
          <span>{vehicles.length ? `1\u2013${vehicles.length} vehicles` : "0 vehicles"}</span>
          {query.hasNextPage && (
            <button
              className="ui-button load-more"
              onClick={() => void query.fetchNextPage()}
              disabled={query.isFetchingNextPage}
            >
              {query.isFetchingNextPage ? "Loading\u2026" : "Load more"}
            </button>
          )}
        </footer>
      </section>
      <VehicleForm
        open={formOpen}
        onClose={() => setFormOpen(false)}
        onSaved={() => {
          setFormOpen(false);
          setToast(true);
          void query.refetch();
        }}
      />
      <Toast open={toast} onClose={() => setToast(false)} dismissLabel="Dismiss" undoLabel="Undo">
        Vehicle added successfully.
      </Toast>
    </div>
  );
}
