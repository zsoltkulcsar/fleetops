import { useQuery } from "@tanstack/react-query";
import {
  CalendarX,
  ChevronLeft,
  ChevronRight,
  CirclePlus,
  ClipboardCheck,
  TriangleAlert,
  Upload,
  UserPlus,
  Wrench,
} from "lucide-react";
import { Plate } from "@fleetops/ui";
import { Link } from "react-router";
import { absenceLabels, absencesApi } from "../absences/api";
import { addDays, berlinToday, mondayOf } from "../people/directory";
import { readinessApi } from "../readiness/api";
import { scheduleApi } from "../schedule/api";
import { formatDocumentDate } from "../shell/date";
import { timesheetsApi } from "../timesheets/api";

export const decisionsPerPage = 8;

function Bar({ parts }: { parts: { value: number; tone: string }[] }) {
  const total = parts.reduce((sum, part) => sum + part.value, 0);
  return (
    <span className="cp-bar" aria-hidden>
      {total > 0 &&
        parts
          .filter((part) => part.value > 0)
          .map((part) => <span key={part.tone} className={part.tone} style={{ flexGrow: part.value }} />)}
    </span>
  );
}

/** Status tiles above the queue. Every number comes from an existing endpoint; a tile whose data fails shows a dash. */
export function StatusTiles({
  open,
  critical,
  onShowQueue,
}: {
  open: number;
  critical: number;
  onShowQueue: (filter: "all" | "critical") => void;
}) {
  const readiness = useQuery({
    queryKey: ["readiness-summary"],
    queryFn: readinessApi.summary,
    refetchInterval: 60_000,
  });
  const week = mondayOf(berlinToday());
  const timesheets = useQuery({
    queryKey: ["control-panel-week", week],
    queryFn: () => timesheetsApi.week(week, "all", "", 1),
    retry: false,
    refetchInterval: 60_000,
  });
  const summary = readiness.data;
  const vehiclesKnown = typeof summary?.vehiclesTotal === "number";
  const driversKnown = typeof summary?.driversTotal === "number";
  const notUsable = vehiclesKnown ? summary!.vehiclesTotal - summary!.vehiclesUsable : 0;
  const blocked = driversKnown ? summary!.driversTotal - summary!.driversAbleToDrive : 0;
  const warnings = Math.max(0, open - critical);
  const counts = timesheets.data?.counts;
  const cutoff = timesheets.data?.cutoff;
  return (
    <section className="cp-tiles" aria-label="Status">
      <Link className="cp-tile" to="/fleet">
        <span className="eyebrow">Vehicles ready</span>
        <strong>
          {vehiclesKnown ? summary!.vehiclesUsable : "—"}{" "}
          {vehiclesKnown && <small>of {summary!.vehiclesTotal}</small>}
        </strong>
        <Bar
          parts={[
            { value: vehiclesKnown ? summary!.vehiclesUsable : 0, tone: "ok" },
            { value: notUsable, tone: "crit" },
          ]}
        />
        <span className="cp-sub">
          {vehiclesKnown ? (notUsable === 0 ? "All vehicles usable" : `${notUsable} not usable`) : "No data"}
        </span>
      </Link>
      <Link className="cp-tile" to="/people">
        <span className="eyebrow">Drivers ready</span>
        <strong>
          {driversKnown ? summary!.driversAbleToDrive : "—"}{" "}
          {driversKnown && <small>of {summary!.driversTotal}</small>}
        </strong>
        <Bar
          parts={[
            { value: driversKnown ? summary!.driversAbleToDrive : 0, tone: "ok" },
            { value: blocked, tone: "crit" },
          ]}
        />
        <span className="cp-sub">
          {driversKnown ? (blocked === 0 ? "All drivers can drive" : `${blocked} blocked`) : "No data"}
        </span>
      </Link>
      <button
        type="button"
        className={`cp-tile${critical > 0 ? " alert" : ""}`}
        onClick={() => onShowQueue(critical > 0 ? "critical" : "all")}
      >
        <span className="eyebrow">Needs a decision</span>
        <strong>
          {open} <small>open</small>
        </strong>
        <Bar
          parts={[
            { value: critical, tone: "crit" },
            { value: warnings, tone: "warn" },
          ]}
        />
        <span className="cp-sub">
          {critical} critical {"·"} {warnings} other
        </span>
      </button>
      <Link className="cp-tile" to="/timesheets?filter=awaiting">
        <span className="eyebrow">Week close</span>
        <strong>
          {counts ? counts.awaiting : "—"} {counts && <small>awaiting approval</small>}
        </strong>
        <Bar
          parts={[
            { value: counts ? Math.max(0, counts.all - counts.awaiting - counts.missing) : 0, tone: "ok" },
            { value: counts?.awaiting ?? 0, tone: "warn" },
            { value: counts?.missing ?? 0, tone: "idle" },
          ]}
        />
        <span className="cp-sub">
          {counts
            ? `${counts.missing} missing${cutoff ? ` · cutoff ${formatDocumentDate(cutoff.date)}` : ""}`
            : "No data"}
        </span>
      </Link>
    </section>
  );
}

/** Shortcuts to the screens where the work is done. Each one opens an existing page; none acts by itself. */
export function QuickActions({ canManagePeople }: { canManagePeople: boolean }) {
  const actions = [
    { label: "Assign driver", to: "/plan", icon: UserPlus },
    { label: "Schedule service", to: "/fleet/maintenance?action=plan", icon: Wrench },
    { label: "Open defects", to: "/fleet/defects", icon: TriangleAlert },
    { label: "Approve shifts", to: "/timesheets?filter=awaiting", icon: ClipboardCheck },
    { label: "Add missing shift", to: "/timesheets?filter=missing", icon: CirclePlus },
    ...(canManagePeople ? [{ label: "Record absence", to: "/people/absences", icon: CalendarX }] : []),
    { label: "Upload document", to: "/documents", icon: Upload },
  ];
  return (
    <section className="cp-panel" aria-labelledby="cp-quick-title">
      <h2 id="cp-quick-title">Quick actions</h2>
      <div className="cp-quick">
        {actions.map((action) => (
          <Link key={action.label} to={action.to}>
            <action.icon size={18} aria-hidden />
            {action.label}
          </Link>
        ))}
      </div>
    </section>
  );
}

/** Handovers and absences for today (Europe/Berlin). A part whose request fails or is not allowed is left out. */
export function TodayPanel() {
  const today = berlinToday();
  const schedule = useQuery({
    queryKey: ["control-panel-schedule", today],
    queryFn: () => scheduleApi.list(addDays(today, -1), addDays(today, 1)),
    retry: false,
  });
  const absences = useQuery({
    queryKey: ["control-panel-absences", today],
    queryFn: () => absencesApi.list(today, today),
    retry: false,
  });
  const handovers = (schedule.data?.items ?? [])
    .filter((cycle) => cycle.from === today || cycle.to === today)
    .sort((left, right) => left.vehiclePlate.localeCompare(right.vehiclePlate));
  const absent = (absences.data?.items ?? []).filter(
    (item) => item.absence.status === "approved" && item.absence.from <= today && item.absence.to >= today,
  );
  if (!schedule.data && !absences.data) return null;
  return (
    <section className="cp-panel" aria-labelledby="cp-today-title">
      <h2 id="cp-today-title">Today</h2>
      {schedule.data && (
        <>
          <span className="eyebrow">Handovers</span>
          {handovers.length === 0 ? (
            <p className="ui-muted">No cycle starts or ends today.</p>
          ) : (
            <ul className="cp-list">
              {handovers.map((cycle) => (
                <li key={cycle.id}>
                  <Plate>{cycle.vehiclePlate}</Plate>
                  <Link to={`/plan?cycleId=${cycle.id}`}>{cycle.employeeName}</Link>
                  <span className="ui-muted">{cycle.from === today ? "starts" : "ends"}</span>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
      {absences.data && (
        <>
          <span className="eyebrow">Absent</span>
          {absent.length === 0 ? (
            <p className="ui-muted">Nobody is absent today.</p>
          ) : (
            <ul className="cp-list">
              {absent.map((item) => (
                <li key={item.absence.id}>
                  <span className="cp-grow">{item.employeeName}</span>
                  <span className="cp-chip">{absenceLabels[item.absence.type] ?? "Absent"}</span>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </section>
  );
}

export function Pagination({
  page,
  total,
  onPage,
}: {
  page: number;
  total: number;
  onPage: (page: number) => void;
}) {
  const pages = Math.ceil(total / decisionsPerPage);
  if (pages <= 1) return null;
  const first = (page - 1) * decisionsPerPage + 1;
  const last = Math.min(total, page * decisionsPerPage);
  return (
    <nav className="cp-pagination" aria-label="Decision pages">
      <span>
        Showing {first} to {last} of {total}
      </span>
      <div>
        <button type="button" className="ui-button" disabled={page === 1} onClick={() => onPage(page - 1)}>
          <ChevronLeft size={16} aria-hidden />
          Previous
        </button>
        {pages <= 7 ? (
          Array.from({ length: pages }, (_, index) => index + 1).map((number) => (
            <button
              key={number}
              type="button"
              className={`ui-button cp-page${number === page ? " primary" : ""}`}
              aria-label={`Page ${number}`}
              aria-current={number === page ? "page" : undefined}
              onClick={() => onPage(number)}
            >
              {number}
            </button>
          ))
        ) : (
          <span>
            Page {page} of {pages}
          </span>
        )}
        <button
          type="button"
          className="ui-button"
          disabled={page === pages}
          onClick={() => onPage(page + 1)}
        >
          Next
          <ChevronRight size={16} aria-hidden />
        </button>
      </div>
    </nav>
  );
}
