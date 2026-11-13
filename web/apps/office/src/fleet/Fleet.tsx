import { useEffect, useMemo, useRef, useState } from "react";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import {
  CheckCircle2,
  ChevronRight,
  Fuel,
  OctagonAlert,
  Plus,
  Search,
  SlidersHorizontal,
  TriangleAlert,
  Wrench,
  X,
} from "lucide-react";
import { Banner, EmptyState, Plate, StatusPill, Toast } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { en } from "../i18n/en";
import { ComingLater } from "../shell/ComingLater";
import { formatDocumentDate } from "../shell/date";
import { scheduleApi } from "../schedule/api";
import { fleetApi, type Vehicle } from "./api";
import { defectApi } from "./defectApi";
import { companyToday, useDefectAccess } from "./defectPresentation";
import { VehicleForm } from "./VehicleForm";
import "./fleet.css";
import "../people/people.css";
import "../timesheets/timesheets.css";
import { readinessApi, type VehicleReadiness } from "../readiness/api";
import { fleetCauseText } from "../readiness/labels";
import { FleetTabs } from "./FleetTabs";
import { VehicleRelease } from "./VehicleRelease";
import { VehicleQuickView, type VehicleTab } from "./VehicleQuickView";

type ReadinessVehicle = Vehicle & VehicleReadiness;
type StatusFilter = "" | "usable" | "unusable" | "workshop";
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

function shownStatus(vehicle: ReadinessVehicle) {
  const shown = vehicle.shownStatus ?? (vehicle.status === "retired" ? "retired" : "active");
  const notUsable = shown === "active" && vehicle.usable === false;
  return {
    shown,
    usable: shown === "active" && !notUsable,
    pill: notUsable ? ("off-road" as const) : statusPillFor[shown],
    label: notUsable ? "Not usable" : statusLabel[shown],
  };
}

export function Fleet() {
  const heading = useRef<HTMLHeadingElement>(null);
  const session = useSession();
  const canEdit = Boolean(session.data?.roles.some((role) => role === "dispatcher" || role === "owner"));
  const { canRead: canReadDefects } = useDefectAccess();
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [kind, setKind] = useState<Vehicle["kind"] | "">("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("");
  const [includeRetired, setIncludeRetired] = useState(false);
  const [formOpen, setFormOpen] = useState(false);
  const [toast, setToast] = useState(false);
  const [quick, setQuick] = useState<{ vehicle: ReadinessVehicle; tab: VehicleTab } | null>(null);
  const today = companyToday();
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
  const defects = useQuery({
    queryKey: ["fleet", "defects", "open", "all"],
    queryFn: () => defectApi.list({ statusGroup: "open", page: 1, pageSize: 100 }),
    enabled: canReadDefects,
    retry: false,
  });
  const repairs = useQuery({
    queryKey: ["fleet", "repairs", "all"],
    queryFn: () => defectApi.repairs(),
    enabled: canReadDefects,
    retry: false,
  });
  const schedule = useQuery({
    queryKey: ["schedule", today, today],
    queryFn: () => scheduleApi.list(today, today),
    retry: false,
  });
  const vehicles = useMemo(
    () => query.data?.pages.flatMap((page) => page.items as ReadinessVehicle[]) ?? [],
    [query.data],
  );
  const complete = Boolean(query.data) && !query.hasNextPage;
  const shown = vehicles.filter((vehicle) => {
    if (!statusFilter) return true;
    const status = shownStatus(vehicle);
    if (statusFilter === "usable") return status.usable;
    if (statusFilter === "workshop") return status.shown === "in_maintenance";
    return !status.usable;
  });
  const driverByVehicle = useMemo(() => {
    const map = new Map<string, string>();
    for (const cycle of schedule.data?.items ?? []) {
      if (!["planned", "active", "overrun"].includes(cycle.status)) continue;
      if (cycle.from <= today && cycle.to >= today) map.set(cycle.vehicleId, cycle.employeeName);
    }
    return map;
  }, [schedule.data, today]);
  const defectsByVehicle = useMemo(() => {
    const map = new Map<string, { open: number; critical: number }>();
    for (const item of defects.data?.items ?? []) {
      const entry = map.get(item.vehicleId) ?? { open: 0, critical: 0 };
      entry.open += 1;
      if (item.severity === "critical") entry.critical += 1;
      map.set(item.vehicleId, entry);
    }
    return map;
  }, [defects.data]);
  const openRepairs = useMemo(
    () =>
      (repairs.data?.items ?? [])
        .filter((item) => ["scheduled", "in_progress"].includes(item.status))
        .sort((a, b) => a.scheduledFor.localeCompare(b.scheduledFor)),
    [repairs.data],
  );
  const nextService = useMemo(() => {
    const map = new Map<string, (typeof openRepairs)[number]>();
    for (const item of openRepairs) if (!map.has(item.vehicleId)) map.set(item.vehicleId, item);
    return map;
  }, [openRepairs]);
  const overdue = openRepairs.filter((item) => item.overdue).length;
  const openDefects = defects.data?.total ?? null;
  const criticalDefects = (defects.data?.items ?? []).filter((item) => item.severity === "critical").length;
  const kindCount = (value: Vehicle["kind"] | "") =>
    complete && !kind && !term ? vehicles.filter((vehicle) => !value || vehicle.kind === value).length : null;
  const statusCount = (value: StatusFilter) =>
    complete
      ? vehicles.filter((vehicle) => {
          const status = shownStatus(vehicle);
          return value === "usable"
            ? status.usable
            : value === "workshop"
              ? status.shown === "in_maintenance"
              : value === "unusable"
                ? !status.usable
                : true;
        }).length
      : null;
  const notUsable = summary.data ? summary.data.vehiclesTotal - summary.data.vehiclesUsable : null;
  const filtered = Boolean(kind || term || statusFilter || includeRetired);
  return (
    <div className="page fleet-page directory-page">
      <div className="page-title people-header">
        <div>
          <div className="people-title-row">
            <h1 ref={heading} tabIndex={-1}>
              Fleet
            </h1>
            <span className="people-title-badge">Trucks & trailers</span>
          </div>
          <p className="ui-muted people-subtitle">
            Which vehicles can drive today, who drives them, open defects and the next service.
          </p>
        </div>
        <div className="people-actions">
          {canReadDefects && (
            <Link className="ui-button" to="/fleet/maintenance">
              <Wrench size={16} aria-hidden /> Maintenance plan
            </Link>
          )}
          {canEdit && (
            <button className="ui-button primary" onClick={() => setFormOpen(true)}>
              <Plus size={17} aria-hidden /> Add vehicle
            </button>
          )}
        </div>
      </div>
      <div className="people-switch-row">
        <FleetTabs
          vehicles={complete && !filtered ? vehicles.length : null}
          openDefects={openDefects}
          overdue={overdue}
        />
        {criticalDefects > 0 && (
          <Link className="people-leave-pill crit" to="/fleet/defects">
            <span className="people-leave-dot" aria-hidden />
            <span>
              <strong>
                {criticalDefects} critical {criticalDefects === 1 ? "defect" : "defects"}
              </strong>{" "}
              {criticalDefects === 1 ? "needs" : "need"} a decision
            </span>
            <ChevronRight size={14} aria-hidden />
          </Link>
        )}
      </div>
      <section className="ts-kpis fleet-kpis" aria-label="Fleet at a glance">
        <div className="ui-panel ts-kpi">
          <span className="ts-kpi-head">
            <span>Ready to drive</span>
            <span className="ts-kpi-icon tone-ok" aria-hidden>
              <CheckCircle2 size={16} />
            </span>
          </span>
          <strong className="tabular-nums">{summary.data ? summary.data.vehiclesUsable : "\u2013"}</strong>
          <small>
            {summary.data
              ? `${summary.data.vehiclesUsable} of ${summary.data.vehiclesTotal} usable`
              : "Summary unavailable"}
          </small>
        </div>
        <div className="ui-panel ts-kpi">
          <span className="ts-kpi-head">
            <span>Not usable</span>
            <span className="ts-kpi-icon tone-crit" aria-hidden>
              <OctagonAlert size={16} />
            </span>
          </span>
          <strong className="tabular-nums">{notUsable ?? "\u2013"}</strong>
          <button
            type="button"
            className="ts-kpi-link"
            disabled={!notUsable}
            onClick={() => setStatusFilter("unusable")}
          >
            Show them
          </button>
        </div>
        <div className="ui-panel ts-kpi">
          <span className="ts-kpi-head">
            <span>Open defects</span>
            <span className="ts-kpi-icon tone-warn" aria-hidden>
              <TriangleAlert size={16} />
            </span>
          </span>
          <strong className="tabular-nums">{canReadDefects ? (openDefects ?? "\u2013") : "\u2013"}</strong>
          <small>
            {canReadDefects ? `${criticalDefects} critical` : "Visible to dispatchers and owners"}
          </small>
        </div>
        <div className="ui-panel ts-kpi">
          <span className="ts-kpi-head">
            <span>Service planned</span>
            <span className="ts-kpi-icon tone-accent" aria-hidden>
              <Wrench size={16} />
            </span>
          </span>
          <strong className="tabular-nums">
            {canReadDefects && repairs.data ? openRepairs.length : "\u2013"}
          </strong>
          <small>{overdue} overdue</small>
        </div>
        <ComingLater title="Fuel & costs">
          <p>
            <Fuel size={14} aria-hidden /> Consumption and cost per km need fuel card data.
          </p>
        </ComingLater>
      </section>
      {query.isError && (
        <Banner label="Error" retryLabel="Try again" onRetry={() => void query.refetch()}>
          Data could not be loaded. Check the connection and try again.
        </Banner>
      )}
      <section className="ui-panel people-filter-card" aria-label="Filters">
        <div className="people-toolbar">
          <div className="people-roles" role="group" aria-label="Vehicle kind">
            {(
              [
                ["", "All"],
                ["truck", "Trucks"],
                ["trailer", "Trailers"],
              ] as const
            ).map(([value, label]) => {
              const count = kindCount(value);
              return (
                <button
                  type="button"
                  key={value}
                  aria-pressed={kind === value}
                  onClick={() => setKind(value)}
                >
                  {label}
                  {count != null && <span className="people-count tabular-nums"> {count}</span>}
                </button>
              );
            })}
          </div>
          <div className="people-search">
            <Search size={16} aria-hidden />
            <label htmlFor="fleet-search" className="visually-hidden">
              Search fleet
            </label>
            <input
              id="fleet-search"
              type="search"
              placeholder="Search by plate"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
            {search && (
              <button
                type="button"
                className="people-search-clear"
                aria-label="Clear search"
                onClick={() => setSearch("")}
              >
                <X size={14} aria-hidden />
              </button>
            )}
          </div>
        </div>
        <div className="people-chips">
          <span className="eyebrow people-chips-label">
            <SlidersHorizontal size={13} aria-hidden /> Status
          </span>
          <div className="people-chip-group" role="group" aria-label="Filter by status">
            {(
              [
                ["", "All statuses"],
                ["usable", "Usable"],
                ["unusable", "Not usable"],
                ["workshop", "In workshop"],
              ] as const
            ).map(([value, label]) => {
              const count = value ? statusCount(value) : null;
              return (
                <button
                  key={label}
                  type="button"
                  className="people-chip"
                  aria-pressed={statusFilter === value}
                  onClick={() => setStatusFilter(value)}
                >
                  {label}
                  {count != null && <span className="tabular-nums"> {count}</span>}
                </button>
              );
            })}
          </div>
          <span className="people-chip-divider" aria-hidden />
          <label className="people-chip check" htmlFor="fleet-include-retired">
            <input
              id="fleet-include-retired"
              type="checkbox"
              checked={includeRetired}
              onChange={(event) => setIncludeRetired(event.target.checked)}
            />
            {en.fleet.includeRetired}
          </label>
          <span className="people-chip-divider" aria-hidden />
          <ComingLater compact title="On a trip" />
          <ComingLater compact title="At the depot" />
          {filtered && (
            <button
              className="ui-button ghost people-clear"
              type="button"
              onClick={() => {
                setKind("");
                setSearch("");
                setStatusFilter("");
                setIncludeRetired(false);
              }}
            >
              <X size={14} aria-hidden /> Clear filters
            </button>
          )}
        </div>
      </section>
      <section className="ui-panel directory-panel" aria-label="Vehicles">
        {query.isLoading ? (
          <div className="fleet-loading">Loading vehicles{"\u2026"}</div>
        ) : !vehicles.length && !query.isError ? (
          <EmptyState
            heading="No vehicles yet."
            primary={canEdit ? { label: "Add vehicle", onClick: () => setFormOpen(true) } : undefined}
          >
            Add the first vehicle to start tracking your fleet.
          </EmptyState>
        ) : !shown.length && !query.isError ? (
          <div className="people-empty">
            <h2>No vehicles with this status</h2>
            <p className="ui-muted">Choose another status or clear the filters.</p>
            <button className="ui-button" type="button" onClick={() => setStatusFilter("")}>
              Show all statuses
            </button>
          </div>
        ) : (
          <div className="table-wrap">
            <table className="people-table fleet-table">
              <caption className="visually-hidden">Fleet vehicles</caption>
              <thead>
                <tr>
                  <th>Vehicle</th>
                  <th>Kind</th>
                  <th>Status</th>
                  <th>Driver today</th>
                  <th>Defects</th>
                  <th>Next service</th>
                  <th>Ownership</th>
                  <th>
                    <span className="visually-hidden">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {shown.map((vehicle) => {
                  const status = shownStatus(vehicle);
                  const driver = driverByVehicle.get(vehicle.id) ?? null;
                  const defectCount = defectsByVehicle.get(vehicle.id);
                  const service = nextService.get(vehicle.id);
                  const open = (tab: VehicleTab) => setQuick({ vehicle, tab });
                  return (
                    <tr
                      key={vehicle.id}
                      tabIndex={0}
                      className={quick?.vehicle.id === vehicle.id ? "selected" : undefined}
                      onClick={(event) => {
                        if (!(event.target instanceof Element && event.target.closest("a, button, input")))
                          open("overview");
                      }}
                      onKeyDown={(event) => {
                        if (event.key === "Enter" && event.target === event.currentTarget) open("overview");
                      }}
                    >
                      <td>
                        <div className="fleet-vehicle">
                          <Plate>{vehicle.plate}</Plate>
                          <span className="ui-muted">
                            {vehicle.make} {vehicle.model} {"\u00b7"}{" "}
                            <span className="tabular-nums">{vehicle.year}</span>
                          </span>
                        </div>
                      </td>
                      <td>{en.fleet.kind[vehicle.kind]}</td>
                      <td>
                        <StatusPill status={status.pill} label={status.label} />
                        {vehicle.causes?.map((cause) => (
                          <div key={`${cause.code}-${cause.documentType}`} className="people-reason">
                            {fleetCauseText(cause)}
                          </div>
                        ))}
                        <VehicleRelease id={vehicle.id} />
                      </td>
                      <td>
                        {driver ? (
                          <span className="fleet-driver">{driver}</span>
                        ) : vehicle.kind === "truck" && vehicle.status !== "retired" ? (
                          <Link className="people-assign" to="/plan">
                            Plan a driver
                          </Link>
                        ) : (
                          <span className="ui-muted">{"\u2014"}</span>
                        )}
                      </td>
                      <td>
                        {!canReadDefects ? (
                          <span className="ui-muted">{"\u2014"}</span>
                        ) : defectCount ? (
                          <button
                            type="button"
                            className="people-cell-button"
                            onClick={() => open("service")}
                          >
                            <span
                              className={`people-doc-summary tone-${defectCount.critical ? "blocked" : "warning"}`}
                            >
                              <TriangleAlert size={14} aria-hidden />
                              {defectCount.open} open
                              {defectCount.critical > 0 && ` \u00b7 ${defectCount.critical} critical`}
                            </span>
                          </button>
                        ) : (
                          <span className="people-doc-summary tone-usable">
                            <CheckCircle2 size={14} aria-hidden /> None open
                          </span>
                        )}
                      </td>
                      <td>
                        {!canReadDefects ? (
                          <span className="ui-muted">{"\u2014"}</span>
                        ) : service ? (
                          <button
                            type="button"
                            className="people-cell-button stacked"
                            onClick={() => open("service")}
                          >
                            <span className="tabular-nums">{formatDocumentDate(service.scheduledFor)}</span>
                            <span className="people-absence-type">
                              {service.type.replaceAll("_", " ")}
                              {service.overdue && <span className="people-flag crit">Overdue</span>}
                            </span>
                          </button>
                        ) : (
                          <span className="ui-muted">Nothing planned</span>
                        )}
                      </td>
                      <td>{en.fleet.ownership[vehicle.ownership]}</td>
                      <td>
                        <div className="people-row-actions">
                          <button
                            type="button"
                            className="people-icon-button"
                            aria-label={`Quick view of ${vehicle.plate}`}
                            title="Quick view"
                            onClick={() => open("overview")}
                          >
                            <ChevronRight size={16} aria-hidden />
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
        <footer className="directory-footer">
          <span>
            {vehicles.length ? `1\u2013${vehicles.length} vehicles` : "0 vehicles"}
            {statusFilter && ` \u00b7 ${shown.length} shown`} {"\u00b7"} Click a row for details
          </span>
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
      {quick && (
        <VehicleQuickView
          vehicle={quick.vehicle}
          tab={quick.tab}
          onTab={(tab) => setQuick({ ...quick, tab })}
          onClose={() => setQuick(null)}
          driver={driverByVehicle.get(quick.vehicle.id) ?? null}
          canReadDefects={canReadDefects}
          status={shownStatus(quick.vehicle)}
        />
      )}
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
