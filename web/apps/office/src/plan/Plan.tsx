import { useEffect, useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, Plus } from "lucide-react";
import { apiClient } from "@fleetops/api-client";
import { Banner, EmptyState, Plate } from "@fleetops/ui";
import { useSearchParams } from "react-router";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";
import { readinessApi } from "../readiness/api";
import { fleetApi } from "../fleet/api";
import { checked } from "../fleet/defectApi";
import { AssignmentEditor } from "../fleet/AssignmentEditor";
import { SwapPicker } from "../fleet/SwapPicker";
import "../fleet/defects.css";
import { absencesApi, absenceLabels } from "../absences/api";
import "../absences/absences.css";
import { scheduleApi, type ScheduleCycle } from "../schedule/api";
import { CycleEditor } from "./CycleEditor";
import { PlanSide } from "./PlanSide";
import { addDays, dateLabel, dayDifference, fullDateLabel, isoWeek, monday, today, validDate } from "./dates";
import { buildRows, candidates, headline, type PlanRow, type ScheduleSwap } from "./model";
import "./plan.css";

type Editor = { mode: ScheduleCycle | "new" | "pattern"; vehicleId?: string };

function span(start: string, from: string, to: string) {
  const offset = Math.max(0, dayDifference(start, from));
  const length = Math.max(1, Math.min(28, dayDifference(start, to) + 1) - offset);
  return { gridColumn: `${offset + 1} / span ${length}` };
}

export function Plan() {
  const [params, setParams] = useSearchParams();
  const start = validDate(params.get("from")) ? params.get("from")! : monday();
  const end = addDays(start, 27);
  const day = today();
  const dayInRange = start <= day && day <= end;
  const view = params.get("view") === "driver" ? "driver" : "truck";
  const [editor, setEditor] = useState<Editor | null>(null);
  const [swap, setSwap] = useState<ScheduleSwap>();
  const [swapFor, setSwapFor] = useState<string>();
  const [saved, setSaved] = useState("");
  const client = useQueryClient();
  const session = useSession();
  const online = useOnline();
  const canEdit = session.data?.roles.some((role) => role === "dispatcher" || role === "owner") === true;
  const query = useQuery({ queryKey: ["schedule", start, end], queryFn: () => scheduleApi.list(start, end) });
  const trucks = useQuery({ queryKey: ["schedule-trucks"], queryFn: () => fleetApi.list({ kind: "truck" }) });
  const drivers = useQuery({
    queryKey: ["readiness-drivers", false],
    queryFn: () => readinessApi.drivers(false),
  });
  const requests = useQuery({
    queryKey: ["schedule-requests"],
    queryFn: () => apiClient.GET("/api/v1/schedule/requests").then(checked),
    enabled: canEdit,
  });
  const absences = useQuery({
    queryKey: ["absences", start, end],
    queryFn: () => absencesApi.list(start, end),
    enabled: view === "driver",
  });
  const rows = useMemo(
    () =>
      buildRows({
        trucks: trucks.data?.items ?? [],
        cycles: query.data?.items ?? [],
        swaps: query.data?.swaps ?? [],
        maintenance: query.data?.maintenance ?? [],
        requests: requests.data?.items ?? [],
        drivers: drivers.data?.items ?? [],
        day,
        dayInRange,
      }),
    [trucks.data, query.data, requests.data, drivers.data, day, dayInRange],
  );
  const options = useMemo(
    () => (dayInRange ? candidates(rows, drivers.data?.items ?? [], day) : []),
    [rows, drivers.data, day, dayInRange],
  );
  const driverNames = useMemo(() => {
    const names = new Map<string, string>();
    for (const driver of drivers.data?.items ?? []) names.set(driver.id, driver.name);
    for (const cycle of query.data?.items ?? []) names.set(cycle.employeeId, cycle.employeeName);
    return names;
  }, [drivers.data, query.data]);
  const fallback = rows.find((row) => row.issue) ?? rows.find((row) => row.requests.length > 0);
  const selected = rows.find((row) => row.id === params.get("truck")) ?? fallback;
  const unmatchedRequests =
    (requests.data?.items.filter((request) => request.status === "open").length ?? 0) -
    rows.reduce((count, row) => count + row.requests.length, 0);
  const update = (name: string, value: string) =>
    setParams((previous) => {
      previous.set(name, value);
      return previous;
    });
  const select = (id: string) => {
    update("truck", id);
    setSaved("");
  };
  const focusedCycle = params.get("cycleId");
  useEffect(() => {
    const cycle = query.data?.items.find((item) => item.id === focusedCycle);
    if (cycle) setEditor({ mode: cycle });
  }, [focusedCycle, query.data]);
  useEffect(() => {
    document.title = "Plan · FleetOps Office";
  }, []);
  const refresh = (message: string) => {
    void client.invalidateQueries({ queryKey: ["schedule"] });
    void client.invalidateQueries({ queryKey: ["schedule-requests"] });
    void client.invalidateQueries({ queryKey: ["attention"] });
    setEditor(null);
    setSaved(message);
  };
  const ready = query.isSuccess && !trucks.isPending;
  const calm = rows.filter((row) => !row.issue && row.requests.length === 0).length;
  const lanes = useMemo(
    () => driverLanes(query.data?.items ?? [], absences.data?.items ?? []),
    [query.data, absences.data],
  );
  return (
    <div className="page plan-page">
      <div className="page-title">
        <div>
          <span className="eyebrow">
            Plan {"·"} {dateLabel(start)} {"–"} {fullDateLabel(end)} {"·"} weeks {isoWeek(start)} to{" "}
            {isoWeek(end)}
          </span>
          <h1 tabIndex={-1}>{ready ? headline(rows) : "Plan"}</h1>
        </div>
        {canEdit && (
          <div className="plan-actions">
            <button className="ui-button" disabled={!online} onClick={() => setEditor({ mode: "pattern" })}>
              Cycle patterns
            </button>
            <button
              className="ui-button primary"
              disabled={!online}
              onClick={() => setEditor({ mode: "new" })}
            >
              <Plus size={17} aria-hidden /> Add cycle
            </button>
          </div>
        )}
      </div>
      {!online && (
        <Banner kind="offline" label="Offline">
          Plan changes need a connection.
        </Banner>
      )}
      {query.isError && (
        <Banner kind="error" label="Error" onRetry={() => void query.refetch()} retryLabel="Try again">
          The plan could not be loaded. Check your connection and try again.
        </Banner>
      )}
      {(trucks.isError || drivers.isError) && (
        <Banner
          kind="error"
          label="Plan incomplete"
          retryLabel="Try again"
          onRetry={() => {
            void trucks.refetch();
            void drivers.refetch();
          }}
        >
          Truck or driver status could not be loaded, so problems may be missing from this plan.
        </Banner>
      )}
      {view === "driver" && absences.isError && (
        <Banner
          kind="error"
          label="Absences could not be loaded"
          retryLabel="Try again"
          onRetry={() => void absences.refetch()}
        >
          The plan may be missing absences. Check the connection and try again.
        </Banner>
      )}
      {saved && (
        <p role="status" className="plan-success">
          {saved}
        </p>
      )}
      <div className="plan-layout">
        <section className="plan-board" aria-label="Four-week plan">
          <div className="plan-toolbar">
            <div className="plan-view" role="group" aria-label="Group plan">
              <button
                className="ui-button"
                aria-pressed={view === "truck"}
                onClick={() => update("view", "truck")}
              >
                By truck
              </button>
              <button
                className="ui-button"
                aria-pressed={view === "driver"}
                onClick={() => update("view", "driver")}
              >
                By driver
              </button>
            </div>
            <div className="plan-range">
              <button
                className="ui-button ghost"
                aria-label="Previous four weeks"
                onClick={() => update("from", addDays(start, -28))}
              >
                <ChevronLeft size={18} />
              </button>
              <span className="tabular-nums">
                {dateLabel(start)} {"–"} {dateLabel(end)}
              </span>
              <button
                className="ui-button ghost"
                aria-label="Next four weeks"
                onClick={() => update("from", addDays(start, 28))}
              >
                <ChevronRight size={18} />
              </button>
            </div>
          </div>
          {query.isPending ? (
            <p className="plan-state" role="status">
              {"Loading plan…"}
            </p>
          ) : query.isError ? null : (view === "truck" ? rows.length : lanes.length) === 0 ? (
            <EmptyState heading="No cycles in these dates">
              Choose another period or add a cycle for a stand-in driver.
            </EmptyState>
          ) : (
            <div className="plan-scroll" tabIndex={0} role="region" aria-label="Plan timeline">
              <div className="plan-timeline">
                <div className="plan-axis">
                  <span>{view === "truck" ? "Truck" : "Driver"}</span>
                  <div className="plan-weeks">
                    {Array.from({ length: 4 }, (_, week) => {
                      const weekStart = addDays(start, week * 7);
                      return (
                        <span key={week}>
                          <strong>Week {isoWeek(weekStart)}</strong> {dateLabel(weekStart)}
                          {weekStart <= day && day <= addDays(weekStart, 6) && (
                            <span className="plan-today">today</span>
                          )}
                        </span>
                      );
                    })}
                  </div>
                </div>
                {view === "truck"
                  ? rows.map((row) => (
                      <TruckLane
                        key={row.id}
                        row={row}
                        start={start}
                        end={end}
                        day={day}
                        dayInRange={dayInRange}
                        selected={selected?.id === row.id}
                        onSelect={() => select(row.id)}
                        onSwap={setSwap}
                      />
                    ))
                  : lanes.map((lane) => (
                      <div className="plan-lane" key={lane.id}>
                        <div className="plan-subject">
                          <strong>{lane.name}</strong>
                        </div>
                        <div className="plan-blocks">
                          {lane.absences.map((item) => (
                            <div
                              key={item.absence.id}
                              className="plan-block"
                              data-kind="absence"
                              style={span(start, item.absence.from, item.absence.to)}
                            >
                              <span>{absenceLabels[item.absence.type]}</span>
                              <small>
                                {dateLabel(item.absence.from)} to {dateLabel(item.absence.to)}
                              </small>
                            </div>
                          ))}
                          {lane.cycles.map((cycle) => (
                            <button
                              key={cycle.id}
                              className="plan-block"
                              data-status={cycle.status}
                              data-source={cycle.source}
                              data-tone={cycle.hasClash || cycle.status === "overrun" ? "issue" : undefined}
                              aria-pressed={selected?.id === cycle.vehicleId}
                              style={span(start, cycle.from, cycle.to)}
                              onClick={() => select(cycle.vehicleId)}
                            >
                              <span>{cycle.vehiclePlate}</span>
                              <small>
                                {dateLabel(cycle.from)} to {dateLabel(cycle.to)}
                                {cycle.hasClash ? " · Clash" : ""}
                              </small>
                            </button>
                          ))}
                        </div>
                      </div>
                    ))}
              </div>
            </div>
          )}
          {ready && (
            <p className="plan-note">
              {calm === 1 ? "1 truck runs" : `${calm} trucks run`} as planned. Cycles roll forward by
              themselves; a manual change is kept.
              {unmatchedRequests > 0 &&
                ` ${unmatchedRequests === 1 ? "1 change request is" : `${unmatchedRequests} change requests are`} for cycles outside these dates: see Attention.`}
            </p>
          )}
        </section>
        <PlanSide
          row={ready ? selected : undefined}
          loading={!ready && !query.isError}
          day={day}
          canEdit={canEdit && online}
          candidates={options}
          driverNames={driverNames}
          onEdit={(cycle) => setEditor({ mode: cycle })}
          onAddCycle={(vehicleId) => setEditor({ mode: "new", vehicleId })}
          onSwapDriver={setSwapFor}
          onDone={refresh}
        />
      </div>
      {swap && <AssignmentEditor key={swap.id} swap={swap} onClose={() => setSwap(undefined)} />}
      {swapFor && (
        <SwapPicker
          key={swapFor}
          vehicleId={swapFor}
          onClose={() => {
            setSwapFor(undefined);
            void client.invalidateQueries({ queryKey: ["schedule"] });
          }}
        />
      )}
      {editor && (
        <CycleEditor
          key={typeof editor.mode === "string" ? `${editor.mode}:${editor.vehicleId ?? ""}` : editor.mode.id}
          mode={editor.mode}
          presetVehicleId={editor.vehicleId}
          canEdit={canEdit && online}
          onClose={() => {
            setEditor(null);
            if (focusedCycle)
              setParams((previous) => {
                previous.delete("cycleId");
                return previous;
              });
          }}
          onSaved={() => refresh("Plan saved. Affected drivers will be told about manual changes.")}
        />
      )}
    </div>
  );
}

type AbsenceItem = Awaited<ReturnType<typeof absencesApi.list>>["items"][number];
function driverLanes(cycles: ScheduleCycle[], absences: AbsenceItem[]) {
  const lanes = new Map<
    string,
    { id: string; name: string; cycles: ScheduleCycle[]; absences: AbsenceItem[] }
  >();
  const lane = (id: string, name: string) => {
    if (!lanes.has(id)) lanes.set(id, { id, name, cycles: [], absences: [] });
    return lanes.get(id)!;
  };
  for (const cycle of cycles)
    if (cycle.status !== "cancelled") lane(cycle.employeeId, cycle.employeeName).cycles.push(cycle);
  for (const item of absences)
    if (item.absence.status === "approved")
      lane(item.absence.employeeId, item.employeeName).absences.push(item);
  return [...lanes.values()].sort((left, right) => left.name.localeCompare(right.name));
}

function TruckLane({
  row,
  start,
  end,
  day,
  dayInRange,
  selected,
  onSelect,
  onSwap,
}: {
  row: PlanRow;
  start: string;
  end: string;
  day: string;
  dayInRange: boolean;
  selected: boolean;
  onSelect: () => void;
  onSwap: (swap: ScheduleSwap) => void;
}) {
  const repair = row.maintenance[0];
  const detail = (cycle: ScheduleCycle) => {
    const asks = row.requests.some((entry) => entry.cycle.id === cycle.id)
      ? " · asks to move this cycle"
      : "";
    const flags = `${cycle.source === "manual" ? " · Manual" : ""}${cycle.hasClash ? " · Clash" : ""}${asks}`;
    if (cycle.status === "overrun") return `Was due back ${dateLabel(cycle.to)}${flags}`;
    if (cycle.id === row.current?.id) {
      if (row.issue?.kind === "off_road")
        return `Truck ${row.issue.label.toLowerCase()} · ${repair ? `workshop ${dateLabel(repair.scheduledFor)}` : "no repair booked"}`;
      if (row.overrun) return `Starts when ${row.overrun.employeeName} is back${flags}`;
      return `until ${dateLabel(cycle.to)}${flags}`;
    }
    if (dayInRange && cycle.from > day && cycle.to > end) return `from ${dateLabel(cycle.from)}${flags}`;
    return `${dateLabel(cycle.from)} to ${dateLabel(cycle.to)}${flags}`;
  };
  return (
    <div className="plan-lane" data-selected={selected} data-issue={row.issue ? true : undefined}>
      <div className="plan-subject">
        <button
          className="plan-truck"
          aria-pressed={selected}
          aria-label={`${row.plate}, ${row.status.label}`}
          onClick={onSelect}
        >
          <Plate>{row.plate}</Plate>
          <span className="plan-status" data-tone={row.status.tone}>
            {row.status.label}
          </span>
        </button>
      </div>
      <div className="plan-blocks">
        {row.maintenance.map((item) => (
          <a
            className="plan-block"
            data-kind="workshop"
            key={item.id}
            style={span(start, item.scheduledFor, item.scheduledFor)}
            href={`/fleet/maintenance?vehicleId=${row.id}`}
          >
            Workshop: {item.type}
          </a>
        ))}
        {row.swaps.map((item) => (
          <button
            className="plan-block"
            data-kind="swap"
            key={item.id}
            style={span(start, item.from, item.to ?? end)}
            onClick={() => onSwap(item)}
          >
            <span>Swap: {item.employeeName}</span>
            <small>
              {dateLabel(item.from)} to {item.to ? dateLabel(item.to) : "Open ended"}
            </small>
          </button>
        ))}
        {row.cycles
          .filter((cycle) => cycle.status !== "cancelled")
          .map((cycle) => {
            const blocked = row.blockedDriver && cycle.id === row.current?.id && !row.swapNow;
            const troubled =
              cycle.hasClash ||
              cycle.status === "overrun" ||
              (cycle.id === row.current?.id && row.issue?.kind === "off_road");
            const until = cycle.status === "overrun" && dayInRange && cycle.to < day ? day : cycle.to;
            return (
              <button
                className="plan-block"
                key={cycle.id}
                data-status={cycle.status}
                data-source={cycle.source}
                data-tone={blocked ? "empty" : troubled ? "issue" : undefined}
                style={span(start, cycle.from, until)}
                onClick={onSelect}
                aria-label={`${cycle.employeeName}, ${row.plate}, ${dateLabel(cycle.from)} to ${dateLabel(cycle.to)}, ${cycle.status}, ${cycle.source}`}
              >
                <span>{blocked ? "No driver" : cycle.employeeName}</span>
                <small>{blocked ? `${cycle.employeeName} is blocked` : detail(cycle)}</small>
              </button>
            );
          })}
        {row.issue?.kind === "no_driver" && !row.issue.cycle && dayInRange && (
          <button
            className="plan-block"
            data-tone="empty"
            style={span(start, day, row.next ? addDays(row.next.from, -1) : end)}
            onClick={onSelect}
          >
            <span>No driver</span>
            <small>No cycle covers today</small>
          </button>
        )}
      </div>
    </div>
  );
}
