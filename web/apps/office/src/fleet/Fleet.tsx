import { useEffect, useMemo, useRef, useState } from "react";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { Plus, Search } from "lucide-react";
import { Banner, EmptyState, Panel, Plate, StatusPill, Toast } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { en } from "../i18n/en";
import { fleetApi, type Vehicle } from "./api";
import { VehicleForm } from "./VehicleForm";
import "./fleet.css";
import { readinessApi, type VehicleReadiness } from "../readiness/api";
import { fleetCauseText } from "../readiness/labels";

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
    document.title = `Fleet · ${en.appName}`;
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
    <div className="page fleet-page">
      <div className="page-title">
        <div>
          <span className="eyebrow">Operations / Fleet</span>
          <h1 ref={heading} tabIndex={-1}>
            Fleet
          </h1>
          <p className="ui-muted">Track every truck and trailer in the workspace.</p>
        </div>
        {canEdit && (
          <button className="ui-button primary" onClick={() => setFormOpen(true)}>
            <Plus size={17} aria-hidden /> Add vehicle
          </button>
        )}
      </div>
      {query.isError && (
        <Banner label="Error" retryLabel="Try again" onRetry={() => void query.refetch()}>
          Data could not be loaded. Check the connection and try again.
        </Banner>
      )}
      <Panel
        title="Vehicles"
        filters={
          <div className="fleet-filters">
            <label className="search-field">
              <Search size={16} aria-hidden />
              <span className="visually-hidden">Search fleet</span>
              <input
                id="fleet-search"
                placeholder="Search by plate"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
              />
            </label>
            <select
              aria-label="Vehicle kind"
              value={kind}
              onChange={(event) => {
                const nextKind = event.target.value;
                if (nextKind === "" || nextKind === "truck" || nextKind === "trailer") setKind(nextKind);
              }}
            >
              <option value="">All vehicles</option>
              <option value="truck">Trucks</option>
              <option value="trailer">Trailers</option>
            </select>
            <label className="checkbox-field">
              <input
                type="checkbox"
                checked={includeRetired}
                onChange={(event) => setIncludeRetired(event.target.checked)}
              />
              {en.fleet.includeRetired}
            </label>
          </div>
        }
      >
        {summary.data && (
          <p className="ui-muted">
            {summary.data.vehiclesUsable} of {summary.data.vehiclesTotal} usable
          </p>
        )}
        {query.isLoading ? (
          <div className="fleet-loading">Loading vehicles…</div>
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
                  <th>Kind</th>
                  <th>Make and model</th>
                  <th>Year</th>
                  <th>Status</th>
                  <th>Ownership</th>
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
                    <td>{en.fleet.kind[vehicle.kind]}</td>
                    <td>
                      <strong>
                        {vehicle.make} {vehicle.model}
                      </strong>
                    </td>
                    <td className="tabular-nums">{vehicle.year}</td>
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
                          </>
                        );
                      })()}
                    </td>
                    <td>{en.fleet.ownership[vehicle.ownership]}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {query.hasNextPage && (
          <button
            className="ui-button load-more"
            onClick={() => void query.fetchNextPage()}
            disabled={query.isFetchingNextPage}
          >
            {query.isFetchingNextPage ? "Loading…" : "Load more"}
          </button>
        )}
      </Panel>
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
