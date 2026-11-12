import { useEffect, useRef, useState } from "react";
import { apiClient } from "@fleetops/api-client";
import { Plate, StatusPill } from "@fleetops/ui";
import { Link } from "react-router";
import { checked, defectApi } from "../fleet/defectApi";
import type { ScheduleCycle } from "../schedule/api";
import { addDays, dateLabel, fullDateLabel } from "./dates";
import type { Candidate, PlanRow, ScheduleRequest } from "./model";

type SideProps = {
  row?: PlanRow;
  loading: boolean;
  day: string;
  canEdit: boolean;
  candidates: Candidate[];
  driverNames: Map<string, string>;
  onEdit: (cycle: ScheduleCycle) => void;
  onAddCycle: (vehicleId: string) => void;
  onSwapDriver: (vehicleId: string) => void;
  onDone: (message: string) => void;
};

export function PlanSide(props: SideProps) {
  const { row, loading } = props;
  const side = useRef<HTMLElement>(null);
  const shown = useRef<string | undefined>(undefined);
  useEffect(() => {
    // When the panel sits below the plan (narrow screens), bring a newly chosen truck into view.
    if (row && shown.current && shown.current !== row.id)
      side.current?.scrollIntoView({ block: "nearest", behavior: "smooth" });
    shown.current = row?.id;
  }, [row]);
  return (
    <aside className="plan-side" ref={side} aria-label="Selected truck">
      {loading ? (
        <section className="plan-card">
          <p role="status">{"Loading truck…"}</p>
        </section>
      ) : !row ? (
        <section className="plan-card">
          <h2>No truck selected</h2>
          <p className="ui-muted">
            Choose a truck in the plan to see its driver, next handover and workshop dates.
          </p>
        </section>
      ) : (
        <>
          {row.issue ? <IssueCard key={row.id} {...props} row={row} /> : <TruckCard {...props} row={row} />}
          {row.requests.map((entry) => (
            <RequestCard
              key={entry.request.id}
              request={entry.request}
              cycle={entry.cycle}
              name={props.driverNames.get(entry.request.employeeId) ?? entry.cycle.employeeName}
              canEdit={props.canEdit}
              onDone={props.onDone}
            />
          ))}
        </>
      )}
    </aside>
  );
}

function CardHead({ row, note }: { row: PlanRow; note: string }) {
  return (
    <p className="plan-card-head">
      <Plate>{row.plate}</Plate>
      <span className="plan-status" data-tone={row.status.tone}>
        {note}
      </span>
    </p>
  );
}

function IssueCard({ row, ...props }: SideProps & { row: PlanRow }) {
  const issue = row.issue!;
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const cycle = issue.cycle;
  const coverUntil = cycle?.to ?? (row.next ? addDays(row.next.from, -1) : null);
  const assign = async (candidate: Candidate) => {
    if (busy || !props.canEdit) return;
    setBusy(true);
    setError("");
    try {
      await defectApi.assign({
        employeeId: candidate.id,
        vehicleId: row.id,
        to: coverUntil,
        maintenanceId: null,
      });
      props.onDone(`${candidate.name} takes ${row.plate}. The driver is told on the phone.`);
    } catch {
      setError("The truck could not be assigned. Refresh the plan, or add a cycle by hand.");
    } finally {
      setBusy(false);
    }
  };
  const record = (
    <Link className="plan-link" to={`/fleet/${row.id}`}>
      Open vehicle record
    </Link>
  );
  if (issue.kind === "no_driver") {
    const shownCandidates = props.candidates.slice(0, 2);
    return (
      <section className="plan-card plan-decision" aria-labelledby="plan-decision-title">
        <CardHead row={row} note={cycle ? `No driver until ${dateLabel(cycle.to)}` : "No driver today"} />
        <h2 id="plan-decision-title">Who takes this truck?</h2>
        <p>{cycle ? `${cycle.employeeName} is blocked.` : "No cycle covers today."} The truck is usable.</p>
        {props.canEdit ? (
          <>
            {shownCandidates.map((candidate) => (
              <button
                key={candidate.id}
                className="plan-option"
                disabled={busy}
                onClick={() => void assign(candidate)}
              >
                <strong>{candidate.name}</strong>
                <small>{candidate.reason}</small>
              </button>
            ))}
            {shownCandidates.length === 0 && <p className="ui-muted">No driver is free today.</p>}
            <button className="ui-button" disabled={busy} onClick={() => props.onAddCycle(row.id)}>
              {shownCandidates.length === 0 ? "Add a cycle" : "Another driver"}
            </button>
            {shownCandidates.length > 0 && (
              <p className="plan-hint">
                One tap assigns{coverUntil ? ` until ${dateLabel(coverUntil)}` : "."} The driver is told on
                the phone.
              </p>
            )}
          </>
        ) : (
          <p className="ui-muted">A dispatcher can assign a driver.</p>
        )}
        {error && (
          <p className="field-error" role="alert">
            {error}
          </p>
        )}
        {record}
      </section>
    );
  }
  if (issue.kind === "off_road") {
    const repair = row.maintenance[0];
    return (
      <section className="plan-card plan-decision" aria-labelledby="plan-decision-title">
        <CardHead row={row} note={issue.label} />
        <h2 id="plan-decision-title">What happens with this truck?</h2>
        <p>
          {cycle!.employeeName} has no usable truck until {dateLabel(cycle!.to)}{" "}
          {repair ? `Workshop is booked for ${dateLabel(repair.scheduledFor)}` : "No repair is booked."}
        </p>
        {props.canEdit ? (
          <>
            <button className="plan-option" onClick={() => props.onSwapDriver(row.id)}>
              <strong>Give {cycle!.employeeName} another truck</strong>
              <small>Pick a free truck for the rest of this cycle.</small>
            </button>
            <Link
              className="plan-option"
              to={`/fleet/maintenance?vehicleId=${row.id}${repair ? "" : "&action=plan"}`}
            >
              <strong>{repair ? "Open the workshop booking" : "Book a repair"}</strong>
              <small>
                {repair
                  ? `${repair.type} on ${dateLabel(repair.scheduledFor)}`
                  : "Plan the workshop visit for this truck."}
              </small>
            </Link>
          </>
        ) : (
          <p className="ui-muted">A dispatcher can swap the driver or book a repair.</p>
        )}
        {record}
      </section>
    );
  }
  if (issue.kind === "late") {
    const waiting = row.current ?? row.next;
    return (
      <section className="plan-card plan-decision" aria-labelledby="plan-decision-title">
        <CardHead row={row} note={`Due back ${dateLabel(cycle!.to)}`} />
        <h2 id="plan-decision-title">The handover is late</h2>
        <p>
          {cycle!.employeeName} was due back {dateLabel(cycle!.to)}{" "}
          {waiting
            ? `${waiting.employeeName} starts when the truck is back.`
            : "No driver is waiting for this truck."}
        </p>
        <button className="plan-option" onClick={() => props.onEdit(cycle!)}>
          <strong>
            {props.canEdit ? "Extend" : "View"} {cycle!.employeeName}
            {"’"}s cycle
          </strong>
          <small>Set the real return date.</small>
        </button>
        {waiting && (
          <button className="plan-option" onClick={() => props.onEdit(waiting)}>
            <strong>
              {props.canEdit ? "Move" : "View"} {waiting.employeeName}
              {"’"}s start
            </strong>
            <small>
              Planned {dateLabel(waiting.from)} to {dateLabel(waiting.to)}
            </small>
          </button>
        )}
        {record}
      </section>
    );
  }
  const clashing = row.cycles.filter((item) => item.hasClash && item.status !== "cancelled").slice(0, 2);
  return (
    <section className="plan-card plan-decision" aria-labelledby="plan-decision-title">
      <CardHead row={row} note="Clash" />
      <h2 id="plan-decision-title">Two cycles overlap</h2>
      <p>A driver or this truck is planned twice on the same days. Change one of the cycles.</p>
      {clashing.map((item) => (
        <button key={item.id} className="plan-option" onClick={() => props.onEdit(item)}>
          <strong>
            {props.canEdit ? "Change" : "View"} {item.employeeName}
            {"’"}s cycle
          </strong>
          <small>
            {dateLabel(item.from)} to {dateLabel(item.to)}
          </small>
        </button>
      ))}
      {record}
    </section>
  );
}

function TruckCard({ row, ...props }: SideProps & { row: PlanRow }) {
  const vehicle = row.vehicle;
  const live = row.cycles.filter((cycle) => cycle.status !== "cancelled");
  const current = row.current;
  const next = row.next;
  const shownCycle = current ?? next ?? live[0];
  const repair = row.maintenance[0];
  const status =
    vehicle === undefined
      ? null
      : vehicle.shownStatus === "in_maintenance"
        ? ("maintenance" as const)
        : vehicle.usable
          ? ("usable" as const)
          : ("off-road" as const);
  return (
    <section className="plan-card" aria-labelledby="plan-truck-title">
      <CardHead row={row} note={row.status.label} />
      <h2 id="plan-truck-title">
        {current ? current.employeeName : row.swapNow ? row.swapNow.employeeName : "No driver today"}
      </h2>
      <dl className="plan-facts">
        {vehicle && (
          <>
            <dt>Vehicle</dt>
            <dd>
              {vehicle.make} {vehicle.model} {"·"} {vehicle.year}
            </dd>
            <dt>Status</dt>
            <dd>{status && <StatusPill status={status} />}</dd>
          </>
        )}
        <dt>Driver now</dt>
        <dd>
          {row.swapNow
            ? `${row.swapNow.employeeName} (swap${row.swapNow.to ? ` until ${dateLabel(row.swapNow.to)}` : ", open ended"})`
            : current
              ? `${current.employeeName}, until ${dateLabel(current.to)}`
              : "Nobody"}
        </dd>
        <dt>Next handover</dt>
        <dd>
          {next
            ? `${next.employeeName} from ${dateLabel(next.from)} to ${dateLabel(next.to)}`
            : "None in these dates"}
        </dd>
        <dt>Workshop</dt>
        <dd>
          {repair ? (
            <Link to={`/fleet/maintenance?vehicleId=${row.id}`}>
              {repair.type}, {fullDateLabel(repair.scheduledFor)}
            </Link>
          ) : (
            "Nothing booked in these dates"
          )}
        </dd>
        <dt>Cycles shown</dt>
        <dd>
          {live.length}
          {live.some((cycle) => cycle.source === "manual") ? " · includes manual changes" : ""}
        </dd>
      </dl>
      <div className="plan-card-actions">
        {shownCycle && (
          <button className="ui-button" onClick={() => props.onEdit(shownCycle)}>
            {props.canEdit ? "Edit" : "View"} {shownCycle.id === current?.id ? "this cycle" : "next cycle"}
          </button>
        )}
        {current && next && (
          <button className="ui-button" onClick={() => props.onEdit(next)}>
            {props.canEdit ? "Edit" : "View"} next cycle
          </button>
        )}
        {!shownCycle && props.canEdit && (
          <button className="ui-button" onClick={() => props.onAddCycle(row.id)}>
            Add a cycle
          </button>
        )}
      </div>
      <Link className="plan-link" to={`/fleet/${row.id}`}>
        Open vehicle record
      </Link>
    </section>
  );
}

function RequestCard({
  request,
  cycle,
  name,
  canEdit,
  onDone,
}: {
  request: ScheduleRequest;
  cycle: ScheduleCycle;
  name: string;
  canEdit: boolean;
  onDone: (message: string) => void;
}) {
  const [declining, setDeclining] = useState(false);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const reasonId = `decline-${request.id}`;
  const decide = async (accept: boolean) => {
    if (busy || !canEdit || (!accept && !reason.trim())) return;
    setBusy(true);
    setError("");
    try {
      await apiClient
        .POST("/api/v1/schedule/requests/{id}/decide", {
          params: { path: { id: request.id } },
          body: { accept, reason: accept ? null : reason.trim() },
        })
        .then(checked);
      setDeclining(false);
      setReason("");
      onDone(accept ? `Change accepted. ${name} is told.` : `Change declined. ${name} is told why.`);
    } catch {
      setError("The request could not be decided. Check for clashes, refresh the plan and try again.");
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="plan-card" aria-label={`Change request from ${name}`}>
      <h2>Change request</h2>
      <p>
        <strong>{name}</strong> asks to move the cycle of {dateLabel(cycle.from)} {"–"} {dateLabel(cycle.to)}{" "}
        to {dateLabel(request.proposedFrom)} {"–"} {fullDateLabel(request.proposedTo)}.
        {request.note ? ` “${request.note}”` : ""}
      </p>
      <p className="plan-hint">Accepting changes this cycle only, not the pattern.</p>
      {canEdit ? (
        <>
          {declining && (
            <div className="schedule-field">
              <label htmlFor={reasonId}>Decline reason</label>
              <textarea
                id={reasonId}
                maxLength={2000}
                value={reason}
                disabled={busy}
                onChange={(event) => setReason(event.target.value)}
              />
            </div>
          )}
          <div className="plan-card-actions">
            {!declining && (
              <button className="ui-button primary" disabled={busy} onClick={() => void decide(true)}>
                Accept
              </button>
            )}
            <button
              className="ui-button"
              disabled={busy || (declining && !reason.trim())}
              onClick={() => (declining ? void decide(false) : setDeclining(true))}
            >
              {declining ? "Confirm decline" : "Decline"}
            </button>
            {declining && (
              <button className="ui-button ghost" disabled={busy} onClick={() => setDeclining(false)}>
                Back
              </button>
            )}
          </div>
        </>
      ) : (
        <p className="ui-muted">A dispatcher can answer this request.</p>
      )}
      {error && (
        <p className="field-error" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
