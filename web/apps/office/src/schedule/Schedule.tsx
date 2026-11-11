import type { components } from "@fleetops/api-client";
import { AssignmentEditor } from "../fleet/AssignmentEditor";
import "../fleet/defects.css";
import { useEffect, useId, useMemo, useState, type FormEvent } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarDays, ChevronLeft, ChevronRight, Plus } from "lucide-react";
import { Banner, Drawer, EmptyState, Panel, Plate } from "@fleetops/ui";
import { useSearchParams } from "react-router";
import { useSession } from "../auth/AuthGate";
import { AuthRequestError } from "../auth/api";
import { useOnline } from "../shell/Shell";
import { readinessApi } from "../readiness/api";
import { fleetApi } from "../fleet/api";
import { scheduleApi, type ScheduleCycle } from "./api";
import "./schedule.css";
import { FleetTabs } from "../fleet/FleetTabs";
import { absencesApi, absenceLabels } from "../absences/api";
import "../absences/absences.css";

const dateValue = (date: Date) => date.toISOString().slice(0, 10);
function addDays(value: string, days: number) {
  const date = new Date(`${value}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return dateValue(date);
}
function dateLabel(value: string) {
  return new Intl.DateTimeFormat("de-DE", { day: "2-digit", month: "2-digit", timeZone: "UTC" }).format(
    new Date(`${value}T12:00:00Z`),
  );
}
function monday() {
  const today = new Intl.DateTimeFormat("en-CA", {
    timeZone: "Europe/Berlin",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
  const date = new Date(`${today}T12:00:00Z`);
  return addDays(today, -((date.getUTCDay() + 6) % 7));
}
const dayDifference = (from: string, to: string) =>
  Math.round((Date.parse(`${to}T12:00:00Z`) - Date.parse(`${from}T12:00:00Z`)) / 86_400_000);

function validDate(value: string | null): value is string {
  if (!value || value.length !== 10 || value < "1900-01-01" || value > "9998-12-01") return false;
  const parsed = new Date(`${value}T12:00:00Z`);
  return Number.isFinite(parsed.getTime()) && dateValue(parsed) === value;
}

export function Schedule() {
  const [params, setParams] = useSearchParams();
  const start = validDate(params.get("from")) ? params.get("from")! : monday();
  const end = addDays(start, 27);
  const view = params.get("view") === "driver" ? "driver" : "truck";
  const [editor, setEditor] = useState<ScheduleCycle | "new" | "pattern" | null>(null);
  const [swap, setSwap] = useState<components["schemas"]["ScheduleSwap"]>();
  const [saved, setSaved] = useState("");
  const client = useQueryClient();
  const session = useSession();
  const online = useOnline();
  const canEdit = session.data?.roles.some((role) => role === "dispatcher" || role === "owner") === true;
  const query = useQuery({ queryKey: ["schedule", start, end], queryFn: () => scheduleApi.list(start, end) });
  const absences = useQuery({
    queryKey: ["absences", start, end],
    queryFn: () => absencesApi.list(start, end),
    enabled: view === "driver",
  });
  const focusedCycle = params.get("cycleId");
  useEffect(() => {
    const cycle = query.data?.items.find((item) => item.id === focusedCycle);
    if (cycle) setEditor(cycle);
  }, [focusedCycle, query.data]);
  useEffect(() => {
    document.title = "Schedule · FleetOps Office";
  }, []);
  const groups = useMemo(() => {
    const result = new Map<
      string,
      { title: string; cycles: ScheduleCycle[]; swaps: components["schemas"]["ScheduleSwap"][] }
    >();
    for (const cycle of query.data?.items ?? []) {
      const id = view === "truck" ? cycle.vehicleId : cycle.employeeId;
      const title = view === "truck" ? cycle.vehiclePlate : cycle.employeeName;
      if (!result.has(id)) result.set(id, { title, cycles: [], swaps: [] });
      result.get(id)!.cycles.push(cycle);
    }
    for (const swap of query.data?.swaps ?? []) {
      const id = view === "truck" ? swap.vehicleId : swap.employeeId;
      const title = view === "truck" ? swap.vehiclePlate : swap.employeeName;
      if (!result.has(id)) result.set(id, { title, cycles: [], swaps: [] });
      result.get(id)!.swaps.push(swap);
    }
    if (view === "driver")
      for (const item of absences.data?.items ?? []) {
        if (item.absence.status !== "approved") continue;
        if (!result.has(item.absence.employeeId))
          result.set(item.absence.employeeId, { title: item.employeeName, cycles: [], swaps: [] });
      }
    return [...result.entries()].sort(([, left], [, right]) => left.title.localeCompare(right.title));
  }, [query.data, view, absences.data]);
  const update = (name: string, value: string) =>
    setParams((previous) => {
      previous.set(name, value);
      return previous;
    });
  const refresh = () => {
    void client.invalidateQueries({ queryKey: ["schedule"] });
    setEditor(null);
    setSaved("Schedule saved. Affected drivers will be told about manual changes.");
  };
  return (
    <div className="page schedule-page">
      <div className="page-title">
        <div>
          <span className="eyebrow">Operations / Schedule</span>
          <h1 tabIndex={-1}>Schedule</h1>
          <p className="ui-muted">Driver cycles, truck handovers and exceptions.</p>
        </div>
        {canEdit && (
          <div className="schedule-actions">
            <button className="ui-button" disabled={!online} onClick={() => setEditor("pattern")}>
              Cycle patterns
            </button>
            <button className="ui-button primary" disabled={!online} onClick={() => setEditor("new")}>
              <Plus size={17} aria-hidden /> Add cycle
            </button>
          </div>
        )}
      </div>
      {view === "driver" && absences.isError && (
        <Banner
          kind="error"
          label="Absences could not be loaded"
          retryLabel="Try again"
          onRetry={() => void absences.refetch()}
        >
          The schedule may be missing absences. Check the connection and try again.
        </Banner>
      )}
      <FleetTabs />
      {!online && (
        <Banner kind="offline" label="Offline">
          Schedule changes need a connection.
        </Banner>
      )}
      {saved && (
        <p role="status" className="schedule-success">
          {saved}
        </p>
      )}
      {query.isError && (
        <Banner kind="error" label="Error" onRetry={() => void query.refetch()} retryLabel="Try again">
          Schedule could not be loaded. Check your connection and try again.
        </Banner>
      )}
      <Panel
        title="Four-week plan"
        filters={
          <div className="schedule-toolbar">
            <div className="schedule-view" role="group" aria-label="Group schedule">
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
            <div className="schedule-range">
              <button
                className="ui-button ghost"
                aria-label="Previous four weeks"
                onClick={() => update("from", addDays(start, -28))}
              >
                <ChevronLeft size={18} />
              </button>
              <span className="tabular-nums">
                {dateLabel(start)} – {dateLabel(end)}
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
        }
      >
        {query.isPending ? (
          <p className="schedule-state" role="status">
            Loading schedule…
          </p>
        ) : groups.length === 0 && !query.isError ? (
          <EmptyState heading="No cycles in these dates">
            Choose another period or add a cycle for a stand-in driver.
          </EmptyState>
        ) : (
          <div className="schedule-scroll" tabIndex={0} role="region" aria-label="Schedule timeline">
            <div className="schedule-timeline">
              <div className="schedule-axis">
                <strong>{view === "truck" ? "Truck" : "Driver"}</strong>
                <div className="schedule-weeks">
                  {Array.from({ length: 4 }, (_, week) => (
                    <span key={week}>{dateLabel(addDays(start, week * 7))}</span>
                  ))}
                </div>
              </div>
              {groups.map(([id, group]) => (
                <div className="schedule-lane" key={id}>
                  <div className="schedule-subject">
                    {view === "truck" ? <Plate>{group.title}</Plate> : <strong>{group.title}</strong>}
                  </div>
                  <div className="schedule-cycles">
                    {view === "driver" &&
                      absences.data?.items
                        .filter(
                          (item) => item.absence.employeeId === id && item.absence.status === "approved",
                        )
                        .map((item) => {
                          const offset = Math.max(0, dayDifference(start, item.absence.from));
                          const length = Math.max(
                            1,
                            Math.min(28, dayDifference(start, item.absence.to) + 1) - offset,
                          );
                          return (
                            <div
                              key={item.absence.id}
                              className="schedule-absence"
                              style={{ gridColumn: `${offset + 1} / span ${length}` }}
                            >
                              {absenceLabels[item.absence.type]}
                              <small>
                                {" "}
                                {dateLabel(item.absence.from)} to {dateLabel(item.absence.to)}
                              </small>
                            </div>
                          );
                        })}
                    {group.swaps.map((item) => {
                      const offset = Math.max(0, dayDifference(start, item.from));
                      const length = Math.max(
                        1,
                        Math.min(28, dayDifference(start, item.to ?? end) + 1) - offset,
                      );
                      return (
                        <button
                          className="schedule-cycle schedule-swap"
                          key={item.id}
                          style={{ gridColumn: `${offset + 1} / span ${length}` }}
                          onClick={() => setSwap(item)}
                        >
                          <span>Swap: {view === "truck" ? item.employeeName : item.vehiclePlate}</span>
                          <small>
                            {dateLabel(item.from)} to {item.to ? dateLabel(item.to) : "Open ended"}
                          </small>
                        </button>
                      );
                    })}
                    {group.cycles.map((cycle) => {
                      const offset = Math.max(0, dayDifference(start, cycle.from));
                      const length = Math.max(1, Math.min(28, dayDifference(start, cycle.to) + 1) - offset);
                      return (
                        <button
                          className="schedule-cycle"
                          key={cycle.id}
                          data-status={cycle.status}
                          data-source={cycle.source}
                          data-clash={cycle.hasClash}
                          style={{ gridColumn: `${offset + 1} / span ${length}` }}
                          onClick={() => setEditor(cycle)}
                          aria-label={`${cycle.employeeName}, ${cycle.vehiclePlate}, ${dateLabel(cycle.from)} to ${dateLabel(cycle.to)}, ${cycle.status}, ${cycle.source}`}
                        >
                          <span>{view === "truck" ? cycle.employeeName : cycle.vehiclePlate}</span>
                          <small>
                            {dateLabel(cycle.from)} – {dateLabel(cycle.to)} · {cycle.status}
                            {cycle.source === "manual" ? " · Manual" : ""}
                            {cycle.hasClash ? " · Clash" : ""}
                          </small>
                        </button>
                      );
                    })}
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
        <p className="schedule-note">
          <CalendarDays size={16} aria-hidden /> Generated cycles roll forward automatically. Manual edits
          keep their slot.
        </p>
      </Panel>
      {swap && <AssignmentEditor key={swap.id} swap={swap} onClose={() => setSwap(undefined)} />}
      {editor && (
        <CycleEditor
          key={typeof editor === "string" ? editor : editor.id}
          mode={editor}
          canEdit={canEdit && online}
          onClose={() => {
            setEditor(null);
            if (focusedCycle)
              setParams((previous) => {
                previous.delete("cycleId");
                return previous;
              });
          }}
          onSaved={refresh}
        />
      )}
    </div>
  );
}

function CycleEditor({
  mode,
  canEdit,
  onClose,
  onSaved,
}: {
  mode: ScheduleCycle | "new" | "pattern";
  canEdit: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const fieldId = useId();
  const cycle = typeof mode === "object" ? mode : undefined;
  const patternMode = mode === "pattern";
  const [employeeId, setEmployeeId] = useState(cycle?.employeeId ?? "");
  const [failure, setFailure] = useState("");
  const [saving, setSaving] = useState(false);
  const drivers = useQuery({
    queryKey: ["readiness-drivers", false],
    queryFn: () => readinessApi.drivers(false),
  });
  const vehicles = useQuery({
    queryKey: ["schedule-trucks"],
    queryFn: () => fleetApi.list({ kind: "truck" }),
  });
  const pattern = useQuery({
    queryKey: ["cycle-pattern", employeeId],
    queryFn: () => scheduleApi.pattern(employeeId),
    enabled: patternMode && !!employeeId,
  });
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canEdit) return;
    const form = new FormData(event.currentTarget);
    setSaving(true);
    setFailure("");
    try {
      if (patternMode) {
        if (!pattern.data) throw new Error("pattern_not_loaded");
        await scheduleApi.savePattern(employeeId, {
          version: pattern.data.version,
          pattern: {
            onWeeks: Number(form.get("onWeeks")),
            offWeeks: Number(form.get("offWeeks")),
            anchorDate: String(form.get("anchorDate")),
            vehicleId: String(form.get("vehicleId")),
          },
        });
      } else {
        const body = {
          employeeId,
          vehicleId: String(form.get("vehicleId")),
          from: String(form.get("from")),
          to: String(form.get("to")),
          reason: String(form.get("reason")),
        };
        if (cycle) await scheduleApi.edit(cycle.id, { ...body, version: cycle.version });
        else await scheduleApi.add(body);
      }
      onSaved();
    } catch (error) {
      setFailure(
        error instanceof AuthRequestError && error.status === 409
          ? "The plan changed. Close this form and reload the schedule before saving again."
          : "The schedule could not be saved. Check the dates, driver and truck, then try again.",
      );
    } finally {
      setSaving(false);
    }
  };
  return (
    <Drawer
      open
      title={patternMode ? "Cycle pattern" : cycle ? "Edit cycle" : "Add cycle"}
      closeLabel="Close schedule form"
      onClose={onClose}
    >
      <form className="schedule-form" onSubmit={submit}>
        {!canEdit && <p className="ui-muted">You can view this cycle. A dispatcher can change it.</p>}
        {cycle?.source === "generated" && (
          <p>Saving makes this cycle manual. Automatic rollover will keep this edit.</p>
        )}
        <div className="schedule-field">
          <label htmlFor={`${fieldId}-driver`}>Driver</label>
          <select
            id={`${fieldId}-driver`}
            aria-label="Driver"
            required
            value={employeeId}
            disabled={!canEdit || saving}
            onChange={(event) => setEmployeeId(event.target.value)}
          >
            <option value="">Choose a driver</option>
            {drivers.data?.items.map((driver) => (
              <option key={driver.id} value={driver.id}>
                {driver.name}
              </option>
            ))}
          </select>
        </div>
        {(drivers.isError || vehicles.isError) && (
          <p role="alert">Driver or truck choices could not be loaded. Close and try again.</p>
        )}
        {patternMode && pattern.isFetching && <p role="status">Loading pattern…</p>}
        {patternMode && pattern.isError && (
          <p role="alert">Pattern could not be loaded. Choose the driver again or retry later.</p>
        )}
        <div
          key={`${patternMode ? `${employeeId}:${pattern.data?.version}` : "cycle"}:${vehicles.isSuccess ? "trucks" : "loading"}`}
          className="schedule-form"
        >
          <div className="schedule-field">
            <label htmlFor={`${fieldId}-truck`}>Truck</label>
            <select
              id={`${fieldId}-truck`}
              aria-label="Truck"
              name="vehicleId"
              required
              defaultValue={cycle?.vehicleId ?? pattern.data?.pattern?.vehicleId ?? ""}
              disabled={!canEdit || saving}
            >
              <option value="">Choose a truck</option>
              {vehicles.data?.items.map((vehicle) => (
                <option key={vehicle.id} value={vehicle.id}>
                  {vehicle.plate}
                </option>
              ))}
            </select>
          </div>
          {patternMode ? (
            <>
              <div className="schedule-form-pair">
                <div className="schedule-field">
                  <label htmlFor={`${fieldId}-on-weeks`}>On weeks</label>
                  <input
                    id={`${fieldId}-on-weeks`}
                    name="onWeeks"
                    type="number"
                    min={1}
                    max={52}
                    required
                    defaultValue={pattern.data?.pattern?.onWeeks ?? 2}
                  />
                </div>
                <div className="schedule-field">
                  <label htmlFor={`${fieldId}-off-weeks`}>Off weeks</label>
                  <input
                    id={`${fieldId}-off-weeks`}
                    name="offWeeks"
                    type="number"
                    min={0}
                    max={52}
                    required
                    defaultValue={pattern.data?.pattern?.offWeeks ?? 2}
                  />
                </div>
              </div>
              <div className="schedule-field">
                <label htmlFor={`${fieldId}-anchor-date`}>Anchor date</label>
                <input
                  id={`${fieldId}-anchor-date`}
                  name="anchorDate"
                  type="date"
                  required
                  defaultValue={pattern.data?.pattern?.anchorDate ?? monday()}
                />
              </div>
              <p className="ui-muted">
                Keeps 12 weeks ahead. Future generated cycles change; manual cycles stay.
              </p>
            </>
          ) : (
            <>
              <div className="schedule-form-pair">
                <div className="schedule-field">
                  <label htmlFor={`${fieldId}-from`}>From</label>
                  <input
                    id={`${fieldId}-from`}
                    name="from"
                    type="date"
                    required
                    defaultValue={cycle?.from ?? monday()}
                    disabled={!canEdit || saving}
                  />
                </div>
                <div className="schedule-field">
                  <label htmlFor={`${fieldId}-to`}>To</label>
                  <input
                    id={`${fieldId}-to`}
                    name="to"
                    type="date"
                    required
                    defaultValue={cycle?.to ?? addDays(monday(), 13)}
                    disabled={!canEdit || saving}
                  />
                </div>
              </div>
              <div className="schedule-field">
                <label htmlFor={`${fieldId}-reason`}>Reason</label>
                <textarea
                  id={`${fieldId}-reason`}
                  name="reason"
                  required
                  maxLength={500}
                  disabled={!canEdit || saving}
                />
              </div>
            </>
          )}
        </div>
        {failure && (
          <p className="field-error" role="alert">
            {failure}
          </p>
        )}
        {canEdit && (
          <button
            className="ui-button primary"
            type="submit"
            disabled={
              saving ||
              !employeeId ||
              drivers.isPending ||
              vehicles.isPending ||
              (patternMode && !pattern.data)
            }
          >
            {saving ? "Saving…" : patternMode ? "Save pattern" : "Save cycle"}
          </button>
        )}
      </form>
    </Drawer>
  );
}
