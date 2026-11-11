import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useSearchParams, useNavigate } from "react-router";
import { apiClient } from "@fleetops/api-client";
import { Banner, Drawer, StatusPill } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";
import { checked } from "../people/api";
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
import "./absences.css";

export function Absences({ employeeId }: { employeeId?: string }) {
  const session = useSession();
  const hr = session.data?.roles.includes("hr") === true;
  const owner = session.data?.roles.includes("owner") === true;
  const canRecord = hr || session.data?.roles.includes("dispatcher") === true;
  const online = useOnline();
  const [params, setParams] = useSearchParams();
  const [month, setMonth] = useState(
    new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Berlin", year: "numeric", month: "2-digit" })
      .format(new Date())
      .slice(0, 7),
  );
  const [view, setView] = useState("month");
  const [recording, setRecording] = useState(false);
  const [editing, setEditing] = useState<AbsenceDetail>();
  const [selected, setSelected] = useState<AbsenceDetail>();
  const [saved, setSaved] = useState(false);
  const from = `${month}-01`;
  const last = new Date(`${from}T12:00:00Z`);
  last.setUTCMonth(last.getUTCMonth() + 1, 0);
  const to = last.toISOString().slice(0, 10);
  const year = from.slice(0, 4);
  const query = useQuery({
    queryKey: ["absences", from, to, employeeId],
    queryFn: () => absencesApi.list(from, to, employeeId),
  });
  const annual = useQuery({
    queryKey: ["absences", "year", year, employeeId],
    queryFn: () => absencesApi.list(`${year}-01-01`, `${year}-12-31`, employeeId),
  });
  const focusedId = params.get("absenceId");
  const focused = useQuery({
    queryKey: ["absences", "detail", focusedId],
    queryFn: () => absencesApi.detail(focusedId!),
    enabled: Boolean(focusedId),
  });
  const settings = useQuery({
    queryKey: ["company-settings"],
    queryFn: () => apiClient.GET("/api/v1/settings").then(checked),
    enabled: hr || owner,
  });
  const drivers = useQuery({
    queryKey: ["absence-drivers"],
    queryFn: () => absencesApi.people("driver"),
    enabled: !employeeId,
  });
  const rows = (employeeId ? annual.data?.items : query.data?.items) ?? [];
  const pending = annual.data?.items.filter((item) => item.absence.status === "requested") ?? [];
  const groups = new Map<string, { name: string; items: AbsenceDetail[] }>();
  for (const driver of drivers.data ?? []) groups.set(driver.id, { name: driver.name, items: [] });
  for (const item of rows) {
    if (!groups.has(item.absence.employeeId))
      groups.set(item.absence.employeeId, { name: item.employeeName, items: [] });
    groups.get(item.absence.employeeId)!.items.push(item);
  }
  const balance = annual.data?.items
    .flatMap((item) => (item.absence.status === "approved" ? item.balancesAfter : item.balancesBefore))
    .find((entry) => entry.year === Number(year));
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
  return (
    <div className={employeeId ? "absences-panel" : "page directory-page"}>
      {!employeeId && (
        <>
          <h1>People</h1>
          <nav className="directory-tabs" aria-label="People">
            <Link to="/people">People</Link>
            <Link to="/people/absences" aria-current="page">
              Absences
            </Link>
          </nav>
        </>
      )}
      <header className="page-title">
        <h2>Absences</h2>
        {canRecord && (
          <button className="ui-button primary" disabled={!online} onClick={() => setRecording(true)}>
            Record absence
          </button>
        )}
      </header>
      {!online && (
        <Banner label="Offline">Cached absences are read-only until the connection returns.</Banner>
      )}
      {saved && <p role="status">Absence saved.</p>}
      {employeeId && (
        <p>
          {balance ? `${balance.remaining} days left in ${year}` : "Days left unavailable for this year."}
        </p>
      )}
      <div className="directory-toolbar absence-toolbar">
        <label>
          Month
          <input
            type="month"
            required
            value={month}
            onChange={(event) => {
              if (/^\d{4}-\d{2}$/.test(event.target.value)) setMonth(event.target.value);
            }}
          />
        </label>
        <button className="ui-button" aria-pressed={view === "month"} onClick={() => setView("month")}>
          Month strip
        </button>
        <button className="ui-button" aria-pressed={view === "list"} onClick={() => setView("list")}>
          List view
        </button>
      </div>
      {query.isLoading && <p role="status">{"Loading absences\u2026"}</p>}
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
      {!query.isLoading && !query.isError && !rows.length && <p>No absences recorded.</p>}
      {view === "list" || employeeId ? (
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
                    {item.absence.halfDay
                      ? ` (${item.absence.halfDay === "am" ? "Morning" : "Afternoon"})`
                      : ""}
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
      ) : (
        <div className="absence-calendar-scroll">
          <table className="absence-calendar">
            <caption>Absences in {month}</caption>
            <thead>
              <tr>
                <th>Driver</th>
                {Array.from({ length: last.getUTCDate() }, (_, day) => {
                  const date = `${month}-${String(day + 1).padStart(2, "0")}`;
                  const holiday = settings.data?.holidays.find((item) => item.date === date);
                  return (
                    <th key={date} data-holiday={Boolean(holiday)} title={holiday?.name}>
                      {day + 1}
                      {holiday && <span className="visually-hidden"> {holiday.name}</span>}
                    </th>
                  );
                })}
              </tr>
            </thead>
            <tbody>
              {[...groups].map(([id, group]) => (
                <tr key={id}>
                  <th>{group.name}</th>
                  {Array.from({ length: last.getUTCDate() }, (_, day) => {
                    const date = `${month}-${String(day + 1).padStart(2, "0")}`;
                    const holiday = settings.data?.holidays.find((item) => item.date === date);
                    return (
                      <td key={date} data-holiday={Boolean(holiday)}>
                        {group.items
                          .filter(
                            (item) =>
                              item.absence.from <= date &&
                              item.absence.to >= date &&
                              item.absence.status !== "cancelled" &&
                              item.absence.status !== "declined",
                          )
                          .map((item) => (
                            <button
                              key={item.absence.id}
                              className="absence-day"
                              data-status={item.absence.status}
                              onClick={() => setSelected(item)}
                              aria-label={`${group.name}, ${absenceLabels[item.absence.type]}, ${formatDocumentDate(date)}, ${statusLabels[item.absence.status]}`}
                            >
                              {absenceLabels[item.absence.type]}
                            </button>
                          ))}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {settings.isError && <p role="status">Company holidays could not be loaded.</p>}
      {!hr && !owner && <p className="ui-muted">Company holiday calendar is unavailable to this role.</p>}
      {hr && (
        <section className="ui-panel absence-requests">
          <h2>Requests</h2>
          {!pending.length && !annual.isLoading && <p>No vacation requests in {year}.</p>}
          {pending.map((item) => (
            <article key={item.absence.id}>
              <h3>
                {item.employeeName} {"\u00b7"} Vacation
              </h3>
              <p>
                {formatDocumentDate(item.absence.from)} to {formatDocumentDate(item.absence.to)} {"\u00b7"}{" "}
                {item.workingDays} working days
              </p>
              <BalanceContext detail={item} />
              <button className="ui-button" onClick={() => setSelected(item)}>
                Review request
              </button>
            </article>
          ))}
        </section>
      )}
      <p className="ui-muted">{dayCounting}</p>
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
