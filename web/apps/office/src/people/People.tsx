import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link, useNavigate } from "react-router";
import {
  CalendarDays,
  CheckCircle2,
  ChevronRight,
  Search,
  ShieldAlert,
  TriangleAlert,
  X,
} from "lucide-react";
import { Banner, Drawer, EmptyState, Plate, StatusPill } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";
import { absenceLabels, absencesApi } from "../absences/api";
import { peopleApi, type PeopleQuery, type PersonRow } from "./api";
import { roleLabels, stateLabels, statusLabels } from "./labels";
import { CanDrive, PersonStatus } from "./PersonStatus";
import { PersonForm } from "./PersonForm";
import { SendEnrolment } from "./SendEnrolment";
import { ContactActions } from "./ContactActions";
import { PersonQuickView, type QuickViewTab } from "./PersonQuickView";
import {
  absenceRange,
  absenceWindowDays,
  addDays,
  berlinToday,
  documentPill,
  initials,
  openAbsences,
  type AbsenceItem,
} from "./directory";
import "../fleet/fleet.css";
import "./people.css";

const roleTabs = [
  { value: undefined, label: "Everyone" },
  { value: "driver", label: "Drivers" },
  { value: "dispatcher", label: "Dispatchers" },
  { value: "hr", label: "HR" },
  { value: "owner", label: "Owners" },
] as const;

function Tile({
  icon,
  label,
  value,
  detail,
  tone,
  pressed,
  onClick,
}: {
  icon: ReactNode;
  label: string;
  value: string;
  detail: string;
  tone: "ok" | "crit" | "warn" | "idle";
  pressed?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      className={`ui-panel people-tile tone-${tone}`}
      aria-pressed={pressed}
      onClick={onClick}
    >
      <span className="people-tile-head">
        <span className="eyebrow">{label}</span>
        <span className="people-tile-icon" aria-hidden>
          {icon}
        </span>
      </span>
      <strong className="tabular-nums">{value}</strong>
      <span className="ui-muted">{detail}</span>
    </button>
  );
}

export function People() {
  const session = useSession();
  const navigate = useNavigate();
  const canAccess = session.data?.roles.some((role) => ["hr", "dispatcher", "owner"].includes(role)) === true;
  const online = useOnline();
  const canManage = session.data?.roles.some((role) => ["hr", "owner"].includes(role)) === true;
  const [filters, setFilters] = useState<PeopleQuery>({ page: 1, pageSize: 50 });
  const [adding, setAdding] = useState(false);
  const [importing, setImporting] = useState(false);
  const [saved, setSaved] = useState("");
  const [quick, setQuick] = useState<{ person: PersonRow; tab: QuickViewTab } | null>(null);
  const today = berlinToday();
  const windowEnd = addDays(today, absenceWindowDays - 1);
  const people = useQuery({ queryKey: ["people", filters], queryFn: () => peopleApi.list(filters) });
  const everyone = useQuery({
    queryKey: ["people", { page: 1, pageSize: 100 }],
    queryFn: () => peopleApi.list({ page: 1, pageSize: 100 }),
  });
  const blocked = useQuery({
    queryKey: ["people", { page: 1, pageSize: 1, blockedOnly: true }],
    queryFn: () => peopleApi.list({ page: 1, pageSize: 1, blockedOnly: true }),
  });
  const expiring = useQuery({
    queryKey: ["people", { page: 1, pageSize: 1, expiringWithin30: true }],
    queryFn: () => peopleApi.list({ page: 1, pageSize: 1, expiringWithin30: true }),
  });
  const absences = useQuery({
    queryKey: ["absences", today, windowEnd],
    queryFn: () => absencesApi.list(today, windowEnd),
    retry: false,
  });
  useEffect(() => {
    document.title = "People \u00b7 FleetOps";
  }, []);
  const filtered = Boolean(
    filters.role || filters.status || filters.search || filters.blockedOnly || filters.expiringWithin30,
  );
  const change = (next: PeopleQuery) => setFilters((current) => ({ ...current, ...next, page: 1 }));
  const reset = () => setFilters({ page: 1, pageSize: 50 });

  const upcoming = useMemo(() => openAbsences(absences.data?.items, today), [absences.data, today]);
  const byPerson = useMemo(() => {
    const map = new Map<string, AbsenceItem[]>();
    for (const item of upcoming) {
      const list = map.get(item.absence.employeeId) ?? [];
      list.push(item);
      map.set(item.absence.employeeId, list);
    }
    return map;
  }, [upcoming]);
  const absentToday = new Set(
    upcoming
      .filter((item) => item.absence.status === "approved" && item.absence.from <= today)
      .map((item) => item.absence.employeeId),
  ).size;
  const absentPeople = new Set(upcoming.map((item) => item.absence.employeeId)).size;
  const awaiting = upcoming.filter((item) => item.absence.status === "requested").length;

  // Role and status counts are exact only while the whole company fits in one page.
  const all = everyone.data && everyone.data.total <= everyone.data.items.length ? everyone.data.items : null;
  const roleCount = (role?: string) =>
    all ? all.filter((person) => !role || person.roles.includes(role)).length : null;
  const statusCount = (status: string) =>
    all ? all.filter((person) => person.status === status).length : null;
  const drivers = all?.filter((person) => person.roles.includes("driver") && person.status !== "left");
  const canDriveCount = drivers?.filter((person) => person.canDrive).length;

  const addButton = canManage && (
    <button type="button" className="ui-button primary" disabled={!online} onClick={() => setAdding(true)}>
      Add person
    </button>
  );
  const statusChips: { value?: string; label: string }[] = [
    { value: undefined, label: "All statuses" },
    ...Object.entries(statusLabels).map(([value, label]) => ({ value, label })),
  ];
  return (
    <div className="page directory-page people-page">
      <header className="page-title">
        <div>
          <h1 tabIndex={-1}>People</h1>
          <p className="ui-muted people-subtitle">Drivers, office staff and their documents in one place.</p>
        </div>
        {addButton}
      </header>
      <div className="directory-tabs" role="group" aria-label="People views">
        <Link to="/people" aria-current="page">
          People
        </Link>
        <Link to="/people/absences">Absences</Link>
      </div>
      {!online && <Banner label="Offline">Cached people are read-only until the connection returns.</Banner>}
      {saved && <Banner label="Saved">{saved}</Banner>}
      {absences.data && absentPeople > 0 && (
        <div className={`people-notice ${awaiting ? "warn" : ""}`} role="note">
          {awaiting ? <TriangleAlert size={16} aria-hidden /> : <CalendarDays size={16} aria-hidden />}
          <span>
            <strong>Next {absenceWindowDays} days:</strong> {absentPeople}{" "}
            {absentPeople === 1 ? "person is" : "people are"} away
            {awaiting > 0 && (
              <>
                , {awaiting} {awaiting === 1 ? "request is" : "requests are"} waiting for a decision
              </>
            )}
            .
          </span>
          <Link to="/people/absences">{awaiting ? "Review requests" : "Open absences"}</Link>
        </div>
      )}
      <div className="people-tiles" role="group" aria-label="People at a glance">
        <Tile
          icon={<CheckCircle2 size={16} />}
          tone="ok"
          label="Drivers able to drive"
          value={canDriveCount == null ? "\u2013" : `${canDriveCount}`}
          detail={drivers ? `of ${drivers.length} drivers` : "Driver count unavailable"}
          pressed={filters.role === "driver" && !filters.blockedOnly && !filters.status}
          onClick={() => setFilters({ page: 1, pageSize: 50, role: "driver" })}
        />
        <Tile
          icon={<ShieldAlert size={16} />}
          tone="crit"
          label="Blocked from driving"
          value={blocked.data ? `${blocked.data.total}` : "\u2013"}
          detail="Document, status or medical reason"
          pressed={filters.blockedOnly === true}
          onClick={() => setFilters({ page: 1, pageSize: 50, blockedOnly: true })}
        />
        <Tile
          icon={<TriangleAlert size={16} />}
          tone="warn"
          label="Documents expiring"
          value={expiring.data ? `${expiring.data.total}` : "\u2013"}
          detail="Within 30 days"
          pressed={filters.expiringWithin30 === true}
          onClick={() => setFilters({ page: 1, pageSize: 50, expiringWithin30: true })}
        />
        <Tile
          icon={<CalendarDays size={16} />}
          tone="idle"
          label="Absent today"
          value={absences.data ? `${absentToday}` : "\u2013"}
          detail={absences.data ? `${awaiting} awaiting a decision` : "Absences unavailable"}
          onClick={() => navigate("/people/absences")}
        />
      </div>
      <section className="ui-panel directory-panel" aria-label="People">
        <div className="people-toolbar">
          <div className="people-roles" role="group" aria-label="Filter by role">
            {roleTabs.map((tab) => {
              const count = roleCount(tab.value);
              return (
                <button
                  key={tab.label}
                  type="button"
                  aria-pressed={filters.role === tab.value}
                  onClick={() => change({ role: tab.value })}
                >
                  {tab.label}
                  {count != null && <span className="people-count tabular-nums"> {count}</span>}
                </button>
              );
            })}
          </div>
          <div className="people-search">
            <Search size={16} aria-hidden />
            <label htmlFor="people-search" className="visually-hidden">
              Search people
            </label>
            <input
              id="people-search"
              type="search"
              placeholder="Name or personnel number"
              value={filters.search ?? ""}
              onChange={(event) => change({ search: event.target.value })}
            />
          </div>
        </div>
        <div className="people-chips">
          <span className="eyebrow">Status</span>
          <div className="people-chip-group" role="group" aria-label="Filter by status">
            {statusChips.map((chip) => {
              const count = chip.value ? statusCount(chip.value) : null;
              return (
                <button
                  key={chip.label}
                  type="button"
                  className="people-chip"
                  aria-pressed={filters.status === chip.value}
                  onClick={() => change({ status: chip.value })}
                >
                  {chip.label}
                  {count != null && <span className="tabular-nums"> {count}</span>}
                </button>
              );
            })}
          </div>
          <span className="people-chip-divider" aria-hidden />
          <label className="people-chip check">
            <input
              type="checkbox"
              checked={filters.blockedOnly ?? false}
              onChange={(event) => change({ blockedOnly: event.target.checked || undefined })}
            />
            Blocked only
          </label>
          <label className="people-chip check">
            <input
              type="checkbox"
              checked={filters.expiringWithin30 ?? false}
              onChange={(event) => change({ expiringWithin30: event.target.checked || undefined })}
            />
            {"Expiring \u226430 days"}
          </label>
          {filtered && (
            <button className="ui-button ghost people-clear" type="button" onClick={reset}>
              <X size={14} aria-hidden /> Clear filters
            </button>
          )}
        </div>
        {people.isError && (
          <Banner
            kind="error"
            label="People could not be loaded"
            retryLabel="Try again"
            onRetry={() => void people.refetch()}
          >
            Check the connection and try again.
          </Banner>
        )}
        {people.isLoading ? (
          <p role="status" className="fleet-loading">
            {"Loading people\u2026"}
          </p>
        ) : people.data?.items.length ? (
          <div className="table-wrap">
            <table className="people-table">
              <caption className="visually-hidden">People</caption>
              <thead>
                <tr>
                  <th>Person</th>
                  <th>Status</th>
                  <th>Can drive</th>
                  <th>Documents</th>
                  <th>Default vehicle</th>
                  <th>Contact</th>
                  <th>Next absence</th>
                  <th>
                    <span className="visually-hidden">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {people.data.items.map((person) => {
                  const isDriver = person.roles.includes("driver");
                  const next = byPerson.get(person.id)?.[0];
                  const requested = byPerson
                    .get(person.id)
                    ?.some((item) => item.absence.status === "requested");
                  const open = (tab: QuickViewTab) => setQuick({ person, tab });
                  return (
                    <tr
                      key={person.id}
                      tabIndex={0}
                      className={quick?.person.id === person.id ? "selected" : undefined}
                      onClick={(event) => {
                        if (!(event.target instanceof Element && event.target.closest("a, button, input")))
                          open("overview");
                      }}
                      onKeyDown={(event) => {
                        if (event.key === "Enter" && event.target === event.currentTarget) open("overview");
                      }}
                    >
                      <td>
                        <div className="people-person">
                          <span className="people-avatar" aria-hidden>
                            {initials(person.name)}
                          </span>
                          <div>
                            <Link to={`/people/${person.id}`}>{person.name}</Link>
                            <div className="people-role-badges">
                              {person.roles.map((role) => (
                                <span key={role} className={`people-role-badge role-${role}`}>
                                  {roleLabels[role] ?? "Other role"}
                                </span>
                              ))}
                            </div>
                          </div>
                        </div>
                      </td>
                      <td>
                        <PersonStatus status={person.status} />
                      </td>
                      <td>
                        {isDriver ? (
                          <CanDrive person={person} />
                        ) : (
                          <span className="ui-muted">Not a driver</span>
                        )}
                      </td>
                      <td>
                        <button
                          type="button"
                          className="people-cell-button stacked"
                          onClick={() => open("documents")}
                        >
                          <span className="tabular-nums">
                            {person.validDocuments} of {person.requiredDocuments} valid
                          </span>
                          {person.worstState && (
                            <StatusPill
                              status={documentPill(person.worstState)}
                              label={stateLabels[person.worstState] ?? "Unknown state"}
                            />
                          )}
                        </button>
                      </td>
                      <td>
                        {person.defaultVehiclePlate ? (
                          <Plate>{person.defaultVehiclePlate}</Plate>
                        ) : (
                          <span className="ui-muted">{isDriver ? "Not assigned" : "\u2014"}</span>
                        )}
                      </td>
                      <td>
                        {person.phone ? (
                          <div className="people-contact">
                            <span className="ui-mono">{person.phone}</span>
                            <ContactActions name={person.name} phone={person.phone} />
                          </div>
                        ) : (
                          <span className="ui-muted">Not recorded</span>
                        )}
                      </td>
                      <td>
                        {absences.isError ? (
                          <span className="ui-muted">{"\u2014"}</span>
                        ) : next ? (
                          <button
                            type="button"
                            className="people-cell-button stacked"
                            onClick={() => open("absences")}
                          >
                            <span>{absenceRange(next.absence.from, next.absence.to)}</span>
                            <span className="people-absence-type">
                              {absenceLabels[next.absence.type]}
                              {requested && <span className="people-flag">Request waiting</span>}
                            </span>
                          </button>
                        ) : (
                          <span className="ui-muted">None planned</span>
                        )}
                      </td>
                      <td>
                        <div className="people-row-actions">
                          {canAccess && online && isDriver && person.status !== "left" && (
                            <SendEnrolment id={person.id} name={person.name} />
                          )}
                          <button
                            type="button"
                            className="people-icon-button"
                            aria-label={`Quick view of ${person.name}`}
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
        ) : (
          !people.isError && (
            <EmptyState heading={filtered ? "No matching people" : "No people yet."}>
              {filtered ? (
                <>
                  <p>Try another name or clear the filters.</p>
                  <button className="ui-button" type="button" onClick={reset}>
                    Clear filters
                  </button>
                </>
              ) : (
                <>
                  <p>Driver: uses the phone app.</p>
                  <p>Dispatcher: plans trucks and approves shifts.</p>
                  <p>HR: people, documents and payroll.</p>
                  <p>Owner: everything and settings.</p>
                  <div className="people-actions">
                    {addButton}
                    {canManage && (
                      <button className="ui-button" type="button" onClick={() => setImporting(true)}>
                        Import from Excel
                      </button>
                    )}
                  </div>
                </>
              )}
            </EmptyState>
          )
        )}
        {people.data && (
          <footer className="directory-footer">
            <span>
              Showing {people.data.items.length} of {people.data.total} people
            </span>
            <div className="people-actions">
              <button
                type="button"
                className="ui-button"
                disabled={people.data.page <= 1}
                onClick={() => setFilters({ ...filters, page: people.data.page - 1 })}
              >
                Previous
              </button>
              <span>Page {people.data.page}</span>
              <button
                type="button"
                className="ui-button"
                disabled={people.data.page * people.data.pageSize >= people.data.total}
                onClick={() => setFilters({ ...filters, page: people.data.page + 1 })}
              >
                Next
              </button>
            </div>
          </footer>
        )}
      </section>
      {quick && (
        <PersonQuickView
          person={quick.person}
          tab={quick.tab}
          onTab={(tab) => setQuick({ ...quick, tab })}
          onClose={() => setQuick(null)}
          absences={byPerson.get(quick.person.id) ?? []}
          absencesFailed={absences.isError}
          today={today}
          canManage={canManage}
        />
      )}
      {adding && (
        <PersonForm
          onClose={() => setAdding(false)}
          onSaved={(result, patternFailed) => {
            setAdding(false);
            setSaved(
              `${result.enrolmentSent ? "Saved. Enrolment SMS sent." : "Saved. Enrolment SMS not sent. Use Resend enrolment on the Access tab."}${patternFailed ? " Cycle pattern was not saved. Open Plan to set it up." : ""}`,
            );
            void people.refetch();
            void everyone.refetch();
          }}
        />
      )}
      <Drawer
        open={importing}
        title="Import from Excel"
        closeLabel="Close"
        onClose={() => setImporting(false)}
      >
        <p>Import from Excel comes later, in Story 14.1. Use Add person for now.</p>
      </Drawer>
    </div>
  );
}
