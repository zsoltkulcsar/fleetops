import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useSearchParams, useNavigate } from "react-router";
import { CalendarDays, ChevronLeft, ChevronRight, List, Plus } from "lucide-react";
import { Banner, Drawer, StatusPill } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";
import { formatDocumentDate } from "../shell/date";
import { AbsenceForm, AbsenceFailure } from "./AbsenceForm";
import {
  AbsenceError,
  absenceLabels,
  absencesApi,
  statusLabels,
  dayCounting,
  type AbsenceDetail,
} from "./api";
import { ShiftDrawer } from "../timesheets/ShiftDrawer";
import { PeopleViews } from "../people/PeopleViews";
import { addDays, berlinToday, initials, mondayOf } from "../people/directory";
import {
  AbsenceCalendar,
  AbsenceLegend,
  AbsenceSummary,
  avatarTone,
  type AbsenceGroup,
} from "./AbsenceCalendar";
import "./absences.css";

function lastOfMonth(month: string) {
  const last = new Date(`${month}-01T12:00:00Z`);
  last.setUTCMonth(last.getUTCMonth() + 1, 0);
  return last.toISOString().slice(0, 10);
}

function shiftMonth(month: string, delta: number) {
  const value = new Date(`${month}-01T12:00:00Z`);
  value.setUTCMonth(value.getUTCMonth() + delta, 1);
  return value.toISOString().slice(0, 7);
}

export function Absences({ employeeId }: { employeeId?: string }) {
  const session = useSession();
  const hr = session.data?.roles.includes("hr") === true;
  const owner = session.data?.roles.includes("owner") === true;
  const canRecord = hr || session.data?.roles.includes("dispatcher") === true;
  const online = useOnline();
  const [params, setParams] = useSearchParams();
  const today = berlinToday();
  const [month, setMonth] = useState(today.slice(0, 7));
  const [view, setView] = useState("month");
  const [recording, setRecording] = useState(false);
  const [editing, setEditing] = useState<AbsenceDetail>();
  const [selected, setSelected] = useState<AbsenceDetail>();
  const [saved, setSaved] = useState(false);
  const from = `${month}-01`;
  const to = lastOfMonth(month);
  const year = from.slice(0, 4);
  const monthName = new Intl.DateTimeFormat("en-GB", {
    month: "long",
    year: "numeric",
    timeZone: "UTC",
  }).format(new Date(`${from}T12:00:00Z`));
  // Whole weeks around the month, so the week headers count full Mon\u2013Fri weeks.
  const calendarFrom = mondayOf(from);
  const calendarTo = addDays(mondayOf(to), 6);
  const query = useQuery({
    queryKey: ["absences", calendarFrom, calendarTo, employeeId],
    queryFn: () => absencesApi.list(calendarFrom, calendarTo, employeeId),
    enabled: !employeeId,
  });
  const annual = useQuery({
    queryKey: ["absences", "year", year, employeeId],
    queryFn: () => absencesApi.list(`${year}-01-01`, `${year}-12-31`, employeeId),
  });
  const thisMonday = mondayOf(today);
  const upcomingTo = addDays(thisMonday, 34);
  const upcoming = useQuery({
    queryKey: ["absences", thisMonday, upcomingTo, undefined],
    queryFn: () => absencesApi.list(thisMonday, upcomingTo),
    enabled: !employeeId,
  });
  const focusedId = params.get("absenceId");
  const focused = useQuery({
    queryKey: ["absences", "detail", focusedId],
    queryFn: () => absencesApi.detail(focusedId!),
    enabled: Boolean(focusedId),
  });
  const settings = useQuery({
    queryKey: ["holidays", from, to],
    queryFn: () => absencesApi.holidays(from, to),
  });
  const drivers = useQuery({
    queryKey: ["absence-drivers"],
    queryFn: () => absencesApi.people("driver"),
    enabled: !employeeId,
  });
  const calendarItems = query.data?.items ?? [];
  const monthItems = calendarItems.filter((item) => item.absence.from <= to && item.absence.to >= from);
  const rows = (employeeId ? annual.data?.items : monthItems) ?? [];
  const pending = annual.data?.items.filter((item) => item.absence.status === "requested") ?? [];
  const groups = new Map<string, AbsenceGroup>();
  for (const driver of drivers.data ?? []) groups.set(driver.id, { name: driver.name, items: [] });
  for (const item of calendarItems) {
    if (!groups.has(item.absence.employeeId))
      groups.set(item.absence.employeeId, { name: item.employeeName, items: [] });
    groups.get(item.absence.employeeId)!.items.push(item);
  }
  const driverIds = new Set((drivers.data ?? []).map((driver) => driver.id));
  const driverNames = new Map((drivers.data ?? []).map((driver) => [driver.id, driver.name]));
  const loading = employeeId ? annual.isLoading : query.isLoading;
  const active = selected ?? focused.data;
  const closeDetail = () => {
    setSelected(undefined);
    setParams(
      (current) => {
        current.delete("absenceId");
        return current;
      },
      { replace: true },
    );
  };
  const onSaved = () => {
    setRecording(false);
    setEditing(undefined);
    closeDetail();
    setSaved(true);
  };
  const list = (
    <div className="table-wrap">
      <table>
        <caption className="visually-hidden">Absences</caption>
        <thead>
          <tr>
            <th>Person</th>
            <th>Type</th>
            <th>Dates</th>
            <th>Days</th>
            <th>Status</th>
            <th>Details</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((item) => (
            <tr key={item.absence.id}>
              <td>{item.employeeName}</td>
              <td>{absenceLabels[item.absence.type]}</td>
              <td>
                {formatDocumentDate(item.absence.from)} to {formatDocumentDate(item.absence.to)}
                {item.absence.halfDay ? ` (${item.absence.halfDay === "am" ? "Morning" : "Afternoon"})` : ""}
              </td>
              <td>{item.workingDays}</td>
              <td>
                <StatusPill
                  status={
                    item.absence.status === "approved"
                      ? "approved"
                      : item.absence.status === "declined"
                        ? "rejected"
                        : item.absence.status === "requested"
                          ? "submitted"
                          : "absent"
                  }
                  label={statusLabels[item.absence.status]}
                />
                {item.absence.declineReason && <p>{item.absence.declineReason}</p>}
              </td>
              <td>
                <button className="ui-button" onClick={() => setSelected(item)}>
                  Open absence
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
  const status = (
    <>
      {loading && <p role="status">{"Loading absences\u2026"}</p>}
      {(query.isError || annual.isError || focused.isError) && (
        <Banner
          kind="error"
          label="Absences could not be loaded"
          retryLabel="Try again"
          onRetry={() => {
            void query.refetch();
            void annual.refetch();
            if (focusedId) void focused.refetch();
          }}
        >
          Check the connection and try again.
        </Banner>
      )}
    </>
  );
  return (
    <div className={employeeId ? "absences-panel" : "page directory-page absences-page"}>
      {!employeeId && (
        <>
          <h1>People</h1>
          <PeopleViews current="absences" />
        </>
      )}
      <header className="page-title abs-title">
        <div>
          <h2>Absences</h2>
          {!employeeId && drivers.data && query.data && (
            <p className="abs-subtitle">
              {drivers.data.length} driver{drivers.data.length === 1 ? "" : "s"} {"\u00b7"} {monthItems.length}{" "}
              absence{monthItems.length === 1 ? "" : "s"} in {monthName}
            </p>
          )}
        </div>
        {canRecord && (
          <button className="ui-button primary" disabled={!online} onClick={() => setRecording(true)}>
            <Plus size={16} aria-hidden />
            Record absence
          </button>
        )}
      </header>
      {!online && (
        <Banner label="Offline">Cached absences are read-only until the connection returns.</Banner>
      )}
      {saved && <p role="status">Absence saved.</p>}
      {employeeId ? (
        <>
          {(hr || owner) && <PersonBalance employeeId={employeeId} year={Number(year)} />}
          {status}
          {!loading && !annual.isError && !rows.length && <p>No absences recorded.</p>}
          {list}
          <p className="ui-muted">{dayCounting}</p>
        </>
      ) : (
        <>
          {upcoming.data && drivers.data && (
            <AbsenceSummary
              items={upcoming.data.items}
              driverIds={driverIds}
              driverNames={driverNames}
              pending={pending}
              today={today}
              canReview={hr}
            />
          )}
          <section className="abs-card" aria-label="Absence calendar">
            <div className="abs-toolbar">
              <div className="abs-month-nav">
                <button
                  type="button"
                  className="ui-button abs-icon-button"
                  aria-label="Previous month"
                  onClick={() => setMonth(shiftMonth(month, -1))}
                >
                  <ChevronLeft size={16} aria-hidden />
                </button>
                <strong className="abs-month" aria-live="polite">
                  {monthName}
                </strong>
                <button
                  type="button"
                  className="ui-button abs-icon-button"
                  aria-label="Next month"
                  onClick={() => setMonth(shiftMonth(month, 1))}
                >
                  <ChevronRight size={16} aria-hidden />
                </button>
                <button
                  type="button"
                  className="ui-button"
                  disabled={month === today.slice(0, 7)}
                  onClick={() => setMonth(today.slice(0, 7))}
                >
                  Today
                </button>
              </div>
              <div className="abs-switch" role="group" aria-label="View">
                <button type="button" aria-pressed={view === "month"} onClick={() => setView("month")}>
                  <CalendarDays size={14} aria-hidden />
                  Calendar
                </button>
                <button type="button" aria-pressed={view === "list"} onClick={() => setView("list")}>
                  <List size={14} aria-hidden />
                  List
                </button>
              </div>
            </div>
            {status}
            {view === "list" ? (
              <>
                {!loading && !query.isError && !rows.length && (
                  <p className="abs-empty">No absences recorded in {monthName}.</p>
                )}
                {rows.length > 0 && list}
                <div className="abs-legend">
                  <span>{dayCounting}</span>
                </div>
              </>
            ) : (
              <>
                <AbsenceCalendar
                  month={month}
                  items={calendarItems}
                  groups={[...groups]}
                  driverIds={driverIds}
                  holidays={settings.data?.items ?? []}
                  today={today}
                  balance={hr || owner ? Number(year) : undefined}
                  onOpen={setSelected}
                />
                {!loading && !query.isError && !groups.size && (
                  <p className="abs-empty">No absences recorded in {monthName}.</p>
                )}
                <AbsenceLegend note={dayCounting} />
              </>
            )}
          </section>
        </>
      )}
      {settings.isError && <p role="status">Company holidays could not be loaded.</p>}
      {hr && (
        <section id="absence-requests" className="abs-requests" aria-labelledby="absence-requests-title">
          <header>
            <h2 id="absence-requests-title">Requests</h2>
            {pending.length > 0 && <span className="abs-count">{pending.length} to review</span>}
          </header>
          {!pending.length && !annual.isLoading && (
            <p className="abs-requests-empty">No vacation requests in {year}.</p>
          )}
          {pending.map((item) => (
            <RequestRow key={item.absence.id} item={item} onReview={() => setSelected(item)} />
          ))}
        </section>
      )}
      {recording && (
        <AbsenceForm employeeId={employeeId} onClose={() => setRecording(false)} onSaved={onSaved} />
      )}
      {editing && <AbsenceForm detail={editing} onClose={() => setEditing(undefined)} onSaved={onSaved} />}
      {active && !editing && (
        <AbsenceReview
          key={active.absence.id}
          detail={active}
          hr={hr}
          online={online}
          onClose={closeDetail}
          onEdit={() => setEditing(active)}
          onSaved={onSaved}
        />
      )}
    </div>
  );
}

function RequestRow({ item, onReview }: { item: AbsenceDetail; onReview: () => void }) {
  const before = item.balancesBefore[0];
  const after = before ? item.balancesAfter.find((balance) => balance.year === before.year) : undefined;
  return (
    <article className="abs-request">
      <span className="abs-avatar" data-tone={avatarTone(item.absence.employeeId)} aria-hidden="true">
        {initials(item.employeeName)}
      </span>
      <div className="abs-request-main">
        <h3>
          {item.employeeName} {"\u00b7"} {absenceLabels[item.absence.type]}
        </h3>
        <p>
          {formatDocumentDate(item.absence.from)} to {formatDocumentDate(item.absence.to)} {"\u00b7"}{" "}
          {item.workingDays} working days
        </p>
      </div>
      <ul className="abs-request-facts" aria-label="Request context">
        {before && (
          <li>
            Balance {before.year}{" "}
            <strong>
              {before.remaining} {"\u2192"} {after?.remaining ?? "?"} days
            </strong>
          </li>
        )}
        {after && after.remaining < 0 && <li data-tone="crit">Over balance by {-after.remaining} days</li>}
        {item.othersAbsentCount ? (
          <li data-tone="warn">
            {item.othersAbsentCount} other{item.othersAbsentCount === 1 ? "" : "s"} absent
            {item.othersAbsentNames.length ? `: ${item.othersAbsentNames.join(", ")}` : ""}
          </li>
        ) : (
          <li data-tone="ok">No one else absent on these dates</li>
        )}
      </ul>
      <button className="ui-button" onClick={onReview}>
        Review request
      </button>
    </article>
  );
}

function PersonBalance({ employeeId, year }: { employeeId: string; year: number }) {
  const balance = useQuery({
    queryKey: ["absences", "balance", employeeId, year],
    queryFn: () => absencesApi.balance(employeeId, year),
  });
  return (
    <p>
      {balance.data
        ? `${balance.data.left} days left in ${year}`
        : balance.isError
          ? "Balance could not be loaded."
          : "Loading balance\u2026"}
    </p>
  );
}

function BalanceContext({ detail }: { detail: AbsenceDetail }) {
  return (
    <>
      <p>{detail.workingDays} working days counted</p>
      {detail.balancesBefore.map((before) => {
        const after = detail.balancesAfter.find((balance) => balance.year === before.year);
        return (
          <div key={before.year}>
            <p>
              {before.year}: Balance before {before.remaining} days {"\u2192"} after{" "}
              {after?.remaining ?? "Unavailable"} days
            </p>
            {after && after.remaining < 0 && (
              <Banner label="Over balance">Over balance by {-after.remaining} days</Banner>
            )}
          </div>
        );
      })}
      <p>
        Others absent on these dates: {detail.othersAbsentCount}
        {detail.othersAbsentNames.length ? ` (${detail.othersAbsentNames.join(", ")})` : ""}
      </p>
    </>
  );
}

function AbsenceReview({
  detail,
  hr,
  online,
  onClose,
  onEdit,
  onSaved,
}: {
  detail: AbsenceDetail;
  hr: boolean;
  online: boolean;
  onClose: () => void;
  onEdit: () => void;
  onSaved: () => void;
}) {
  const navigate = useNavigate();
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<AbsenceError>();
  const [shiftId, setShiftId] = useState<string>();
  const [retryApprove, setRetryApprove] = useState(false);
  const client = useQueryClient();
  const save = async (action: "approve" | "decline" | "cancel", confirmOverlap = false) => {
    if (!hr || !online || busy || (action !== "approve" && !reason.trim())) return;
    setBusy(true);
    setError(undefined);
    setRetryApprove(action === "approve");
    try {
      if (action === "cancel") await absencesApi.cancel(detail.absence.id, reason.trim());
      else
        await absencesApi.decide(detail.absence.id, {
          approve: action === "approve",
          reason: action === "decline" ? reason.trim() : null,
          confirmOverlap,
        });
      await client.invalidateQueries({ queryKey: ["absences"] });
      await client.invalidateQueries({ queryKey: ["attention"] });
      await client.invalidateQueries({ queryKey: ["schedule"] });
      onSaved();
    } catch (failure) {
      setError(failure instanceof AbsenceError ? failure : new AbsenceError("request_failed", [], []));
    } finally {
      setBusy(false);
    }
  };
  return (
    <>
      <Drawer
        open={!shiftId}
        title={`${detail.employeeName}: ${absenceLabels[detail.absence.type]}`}
        closeLabel="Close absence"
        onClose={() => {
          if (!busy) onClose();
        }}
      >
        <div className="people-form">
          <p>
            {formatDocumentDate(detail.absence.from)} to {formatDocumentDate(detail.absence.to)}
          </p>
          <p>
            <StatusPill
              status={
                detail.absence.status === "approved"
                  ? "approved"
                  : detail.absence.status === "declined"
                    ? "rejected"
                    : detail.absence.status === "requested"
                      ? "submitted"
                      : "absent"
              }
              label={statusLabels[detail.absence.status]}
            />
          </p>
          {detail.absence.note && <p>{detail.absence.note}</p>}
          {detail.absence.declineReason && <p>Decline reason: {detail.absence.declineReason}</p>}
          <BalanceContext detail={detail} />
          {hr && (
            <>
              <label>
                Reason (required to decline or cancel)
                <textarea
                  maxLength={2000}
                  value={reason}
                  onChange={(event) => setReason(event.target.value)}
                />
              </label>
              {detail.absence.status === "requested" && (
                <div className="people-actions">
                  <button
                    className="ui-button primary"
                    disabled={!online || busy}
                    onClick={() => void save("approve")}
                  >
                    Approve
                  </button>
                  <button
                    className="ui-button"
                    disabled={!online || busy || !reason.trim()}
                    onClick={() => void save("decline")}
                  >
                    Decline
                  </button>
                </div>
              )}
              {detail.absence.status === "approved" && (
                <button
                  className="ui-button"
                  disabled={!online || busy || !reason.trim()}
                  onClick={() => void save("cancel")}
                >
                  Cancel absence
                </button>
              )}
              {["requested", "approved"].includes(detail.absence.status) && (
                <button className="ui-button" disabled={!online || busy} onClick={onEdit}>
                  Edit absence
                </button>
              )}
            </>
          )}
          {error && <AbsenceFailure error={error} onShift={setShiftId} />}
          {error?.code === "absence_overlaps_shifts" && retryApprove && (
            <button
              className="ui-button"
              disabled={!online || busy}
              onClick={() => void save("approve", true)}
            >
              Save anyway
            </button>
          )}
          <p>{dayCounting}</p>
        </div>
      </Drawer>
      {shiftId && (
        <ShiftDrawer
          id={shiftId}
          zone="Europe/Berlin"
          onClose={() => setShiftId(undefined)}
          onSaved={() => {
            setShiftId(undefined);
            setError(undefined);
          }}
          onApprove={(shift) => navigate(`/timesheets?shiftId=${shift.id}`)}
        />
      )}
    </>
  );
}
