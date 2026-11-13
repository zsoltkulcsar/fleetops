import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import {
  CheckCircle2,
  ChevronRight,
  Clock,
  Download,
  OctagonAlert,
  Search,
  SearchX,
  SlidersHorizontal,
  TriangleAlert,
  UserPlus,
  X,
} from "lucide-react";
import { Banner, Drawer, EmptyState, Plate, StatusPill } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";
import { ComingLater } from "../shell/ComingLater";
import { absenceLabels, absencesApi } from "../absences/api";
import { timesheetsApi } from "../timesheets/api";
import { PeopleError, peopleApi, type PeopleQuery, type PersonRow } from "./api";
import { roleLabels, stateLabels, statusLabels } from "./labels";
import { CanDrive } from "./PersonStatus";
import { PersonForm } from "./PersonForm";
import { SendEnrolment } from "./SendEnrolment";
import { ContactActions, MessageButton } from "./ContactActions";
import { MessageComposer } from "./MessageComposer";
import { currentAbsence, DemoTag, LiveStatus, liveState } from "./live";
import { PersonQuickView, type QuickViewTab } from "./PersonQuickView";
import { PeopleViews } from "./PeopleViews";
import { CapacityStrip } from "./Capacity";
import {
  absenceRange,
  absenceWindowDays,
  addDays,
  berlinToday,
  documentPill,
  initials,
  mondayOf,
  openAbsences,
  shortDate,
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

const capacityWeeks = 5;
/** Vacation balances are read one person at a time (a list field is Story 15.2); keep the burst small. */
const balanceWorkers = 4;
type VacationBalance = Awaited<ReturnType<typeof absencesApi.balance>>;

function LeaveBalance({ balance }: { balance: VacationBalance | null | undefined }) {
  const total = balance ? balance.entitlement + balance.opening : 0;
  if (!balance || total <= 0) return null;
  const share = Math.max(0, Math.min(100, Math.round((balance.remaining / total) * 100)));
  return (
    <div className="people-leave-balance">
      <strong className="tabular-nums">
        {balance.remaining} / {total} days left
      </strong>
      <span className="people-leave-bar" aria-hidden>
        <span style={{ width: `${share}%` }} />
      </span>
    </div>
  );
}

function DocumentSummary({ person }: { person: PersonRow }) {
  if (!person.requiredDocuments) return <span className="ui-muted">None required</span>;
  const state = person.worstState;
  const Icon =
    state === "verified" ? CheckCircle2 : documentPill(state) === "blocked" ? OctagonAlert : TriangleAlert;
  return (
    <span className={`people-doc-summary tone-${documentPill(state)}`}>
      <Icon size={14} aria-hidden />
      <span className="tabular-nums">
        {person.validDocuments} of {person.requiredDocuments} valid
      </span>
      {state && state !== "verified" && (
        <StatusPill status={documentPill(state)} label={stateLabels[state] ?? "Unknown state"} />
      )}
    </span>
  );
}

export function People() {
  const session = useSession();
  const roles = session.data?.roles ?? [];
  const canAccess = roles.some((role) => ["hr", "dispatcher", "owner"].includes(role));
  const canManage = roles.some((role) => ["hr", "owner"].includes(role));
  const online = useOnline();
  const [filters, setFilters] = useState<PeopleQuery>({ page: 1, pageSize: 50 });
  const [adding, setAdding] = useState(false);
  const [importing, setImporting] = useState(false);
  const [saved, setSaved] = useState("");
  const [quick, setQuick] = useState<{ person: PersonRow; tab: QuickViewTab } | null>(null);
  const [messageTo, setMessageTo] = useState<PersonRow | null>(null);
  const today = berlinToday();
  const thisMonday = mondayOf(today);
  const windowEnd = addDays(thisMonday, capacityWeeks * 7 - 1);
  const people = useQuery({
    queryKey: ["people", filters],
    queryFn: () => peopleApi.list(filters),
    placeholderData: (previous) => previous,
  });
  const everyone = useQuery({
    queryKey: ["people", { page: 1, pageSize: 100 }],
    queryFn: () => peopleApi.list({ page: 1, pageSize: 100 }),
  });
  const absences = useQuery({
    queryKey: ["absences", thisMonday, windowEnd],
    queryFn: () => absencesApi.list(thisMonday, windowEnd),
    retry: false,
  });
  const week = useQuery({
    queryKey: ["timesheets", thisMonday, "all", "", 1],
    queryFn: () => timesheetsApi.week(thisMonday, "all", "", 1),
    enabled: canAccess,
    retry: false,
  });
  const year = Number(today.slice(0, 4));
  const pageIds = useMemo(() => (people.data?.items ?? []).map((person) => person.id), [people.data]);
  const balances = useQuery({
    queryKey: ["vacation-balances", year, pageIds],
    enabled: canAccess && pageIds.length > 0,
    staleTime: 5 * 60_000,
    retry: false,
    queryFn: async () => {
      const result: Record<string, VacationBalance | null> = {};
      let next = 0;
      let denied = false;
      const worker = async () => {
        while (!denied && next < pageIds.length) {
          const id = pageIds[next++];
          try {
            result[id] = await absencesApi.balance(id, year);
          } catch (error) {
            result[id] = null;
            if (error instanceof PeopleError && error.status === 403) denied = true;
          }
        }
      };
      await Promise.all(Array.from({ length: balanceWorkers }, worker));
      return result;
    },
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
  const soonEnd = addDays(today, absenceWindowDays - 1);
  const byPerson = useMemo(() => {
    const map = new Map<string, AbsenceItem[]>();
    for (const item of upcoming) {
      if (item.absence.from > soonEnd) continue;
      const list = map.get(item.absence.employeeId) ?? [];
      list.push(item);
      map.set(item.absence.employeeId, list);
    }
    return map;
  }, [upcoming, soonEnd]);
  const requests = upcoming.filter((item) => item.absence.status === "requested");

  // Role and status counts are exact only while the whole company fits in one page.
  const all = everyone.data && everyone.data.total <= everyone.data.items.length ? everyone.data.items : null;
  const roleCount = (role?: string) =>
    all ? all.filter((person) => !role || person.roles.includes(role)).length : null;
  const statusCount = (status: string) =>
    all ? all.filter((person) => person.status === status).length : null;
  const driverIds = useMemo(
    () =>
      new Set(
        (all ?? [])
          .filter((person) => person.roles.includes("driver") && person.status === "active")
          .map((person) => person.id),
      ),
    [all],
  );

  const addButton = canManage && (
    <button type="button" className="ui-button primary" disabled={!online} onClick={() => setAdding(true)}>
      <UserPlus size={16} aria-hidden />
      Add person
    </button>
  );
  const statusChips: { value?: string; label: string }[] = [
    { value: undefined, label: "All statuses" },
    ...Object.entries(statusLabels).map(([value, label]) => ({ value, label })),
  ];
  return (
    <div className="page directory-page people-page">
      <header className="page-title people-header">
        <div>
          <div className="people-title-row">
            <h1 tabIndex={-1}>People</h1>
            <span className="people-title-badge">Fleet HR & hours</span>
          </div>
          <p className="ui-muted people-subtitle">
            Who can drive, document expiry, absences and weekly hours in one place.
          </p>
        </div>
        <div className="people-actions">
          {canManage && (
            <Link className="ui-button" to="/payroll">
              <Download size={16} aria-hidden />
              Payroll export
            </Link>
          )}
          {addButton}
        </div>
      </header>
      <div className="people-switch-row">
        <PeopleViews current="people" people={roleCount()} awaitingShifts={week.data?.counts.awaiting} />
        {requests.length > 0 && (
          <Link className="people-leave-pill" to="/people/absences">
            <span className="people-leave-dot" aria-hidden />
            <span>
              <strong>
                {requests.length} leave {requests.length === 1 ? "request" : "requests"}
              </strong>{" "}
              {requests.length === 1 ? "is" : "are"} waiting for a decision
            </span>
            <ChevronRight size={14} aria-hidden />
          </Link>
        )}
      </div>
      {absences.data && all && driverIds.size > 0 && (
        <CapacityStrip
          absences={absences.data.items}
          driverIds={driverIds}
          firstMonday={thisMonday}
          weeks={capacityWeeks}
        />
      )}
      {!online && <Banner label="Offline">Cached people are read-only until the connection returns.</Banner>}
      {saved && <Banner label="Saved">{saved}</Banner>}
      <section className="ui-panel people-filter-card" aria-label="Filters">
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
            {filters.search && (
              <button
                type="button"
                className="people-search-clear"
                aria-label="Clear search"
                onClick={() => change({ search: undefined })}
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
          <span className="people-chip-divider" aria-hidden />
          <ComingLater compact title="On road" />
          <ComingLater compact title="Resting" />
          {filtered && (
            <button className="ui-button ghost people-clear" type="button" onClick={reset}>
              <X size={14} aria-hidden /> Clear filters
            </button>
          )}
        </div>
      </section>
      <section className="ui-panel directory-panel" aria-label="People">
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
            <table className="people-table" aria-busy={people.isFetching || undefined}>
              <caption className="visually-hidden">People</caption>
              <thead>
                <tr>
                  <th scope="col">Person & role</th>
                  <th scope="col">Live status & position</th>
                  <th scope="col">Can drive</th>
                  <th scope="col">Default vehicle</th>
                  <th scope="col">Contact</th>
                  <th scope="col">Documents</th>
                  <th scope="col">Leave / balance</th>
                  <th scope="col">
                    <span className="visually-hidden">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {people.data.items.map((person) => {
                  const isDriver = person.roles.includes("driver");
                  const next = byPerson.get(person.id)?.[0];
                  const onLeave = currentAbsence(byPerson.get(person.id), today);
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
                            <div className="people-name">
                              <Link to={`/people/${person.id}`}>{person.name}</Link>
                              {requested && (
                                <span className="people-name-dot" title="Leave request waiting">
                                  <span className="visually-hidden">Leave request waiting</span>
                                </span>
                              )}
                            </div>
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
                        <LiveStatus
                          person={person}
                          state={liveState(person, today, byPerson.get(person.id), week.data?.rows)}
                        />
                      </td>
                      <td>
                        {isDriver ? (
                          <CanDrive person={person} />
                        ) : (
                          <span className="ui-muted">Not a driver</span>
                        )}
                      </td>
                      <td>
                        {person.defaultVehiclePlate ? (
                          <Plate>{person.defaultVehiclePlate}</Plate>
                        ) : isDriver ? (
                          <Link className="people-assign" to="/plan">
                            Assign in Plan
                          </Link>
                        ) : (
                          <span className="ui-muted">{"\u2014 Not relevant"}</span>
                        )}
                      </td>
                      <td>
                        {person.phone ? (
                          <div className="people-contact">
                            <span className="ui-mono">{person.phone}</span>
                            <ContactActions
                              name={person.name}
                              phone={person.phone}
                              onMessage={() => setMessageTo(person)}
                            />
                          </div>
                        ) : (
                          <div className="people-contact">
                            <span className="ui-muted">Not recorded</span>
                            <span className="people-contact-actions">
                              <MessageButton name={person.name} onMessage={() => setMessageTo(person)} />
                            </span>
                          </div>
                        )}
                      </td>
                      <td>
                        <button
                          type="button"
                          className="people-cell-button"
                          onClick={() => open("documents")}
                        >
                          <DocumentSummary person={person} />
                        </button>
                      </td>
                      <td>
                        <LeaveBalance balance={balances.data?.[person.id]} />
                        {absences.isError ? (
                          <span className="ui-muted">{"\u2014"}</span>
                        ) : onLeave ? (
                          <button
                            type="button"
                            className="people-cell-button people-leave-active"
                            onClick={() => open("leave")}
                          >
                            <span className="people-leave-dot" aria-hidden />
                            On leave until {shortDate(onLeave.absence.to)}
                          </button>
                        ) : next ? (
                          <button
                            type="button"
                            className="people-cell-button stacked"
                            onClick={() => open("leave")}
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
                          {isDriver && (
                            <Link
                              className="people-icon-button"
                              to={`/timesheets?search=${encodeURIComponent(person.name)}`}
                              aria-label={`Timesheet of ${person.name}`}
                              title="Timesheet"
                            >
                              <Clock size={16} aria-hidden />
                            </Link>
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
          !people.isError &&
          (filtered ? (
            <div className="people-empty">
              <span className="people-empty-icon" aria-hidden>
                <SearchX size={22} />
              </span>
              <h2>No matching people</h2>
              <p className="ui-muted">Try another name or clear the filters.</p>
              <button className="ui-button" type="button" onClick={reset}>
                Clear filters
              </button>
            </div>
          ) : (
            <EmptyState heading="No people yet.">
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
            </EmptyState>
          ))
        )}
        {people.data && week.data && (
          <p className="people-demo-note">
            <DemoTag /> Position and ETA are sample values. FleetOps has no tachograph or GPS source yet;
            connecting one is optional and replaces the samples (Epic 15). Status comes from shifts and
            approved absences.
          </p>
        )}
        {people.data && (
          <footer className="directory-footer">
            <span>
              Showing {people.data.items.length} of {people.data.total} people {"\u00b7"} Click a row for
              details
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
          absences={upcoming.filter((item) => item.absence.employeeId === quick.person.id)}
          absencesFailed={absences.isError}
          today={today}
          canManage={canManage}
          onMessage={() => {
            // One dialog at a time: the drawer closes before the composer opens.
            setMessageTo(quick.person);
            setQuick(null);
          }}
        />
      )}
      {messageTo && <MessageComposer name={messageTo.name} onClose={() => setMessageTo(null)} />}
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
