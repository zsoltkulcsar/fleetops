import { useEffect, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Banner, Drawer, Panel, Skeleton, StatusPill } from "@fleetops/ui";
import { ChevronLeft, ChevronRight, Plus, TriangleAlert } from "lucide-react";
import { useSearchParams } from "react-router";
import { useSession } from "../auth/AuthGate";
import { AuthRequestError } from "../auth/api";
import { useOnline } from "../shell/Shell";
import { timesheetsApi, type Schema } from "./api";
import { addDays, dateLabel, hours, monday, ShiftStatus } from "./presentation";
import { ShiftDrawer } from "./ShiftDrawer";
import { ShiftEditor } from "./ShiftEditor";
import "./timesheets.css";

export function Timesheets() {
  const [params, setParams] = useSearchParams();
  const session = useSession();
  const online = useOnline();
  const allowed = session.data?.roles.some((role) => ["dispatcher", "hr", "owner"].includes(role)) === true;
  const client = useQueryClient();
  const [week, setWeek] = useState(() => monday("Europe/Berlin"));
  const [filter, setFilter] = useState("all");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string[]>([]);
  const [focus, setFocus] = useState(-1);
  const [opened, setOpened] = useState<string | undefined>(() => params.get("shiftId") ?? undefined);
  const [driver, setDriver] = useState<Schema["WeekRow"]>();
  const [adding, setAdding] = useState<{ employeeId: string; date: string }>();
  const [approval, setApproval] = useState<Schema["GridShift"][]>();
  const [notes, setNotes] = useState<Record<string, string>>({});
  const [result, setResult] = useState<Schema["BulkApprovalResult"]>();
  const [failed, setFailed] = useState<Schema["BulkApprovalItem"][]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const searchInput = useRef<HTMLInputElement>(null);
  const grid = useRef<HTMLTableElement>(null);
  const [shortcuts, setShortcuts] = useState(() => {
    try {
      return localStorage.getItem("fleetops.timesheets.shortcuts") !== "off";
    } catch {
      return true;
    }
  });
  const query = useQuery({
    queryKey: ["timesheets", week, filter, search, page],
    queryFn: () => timesheetsApi.week(week, filter, search, page),
    enabled: allowed,
  });
  const rows = query.data?.rows ?? [];
  const zone = query.data?.companyTimeZone;
  const refresh = () => {
    void client.invalidateQueries({ queryKey: ["timesheets"] });
  };
  const reset = () => {
    setSelected([]);
    setFocus(-1);
    setPage(1);
  };
  const requestApproval = (targets: Schema["WeekRow"][]) => {
    setError("");
    const own = targets.some((row) => row.employeeId === session.data?.employeeId);
    const shifts = targets
      .filter((row) => row.employeeId !== session.data?.employeeId)
      .flatMap((row) =>
        row.days.flatMap((day) => day.shifts.filter((shift) => shift.status === "submitted")),
      );
    if (own)
      setError("Your own shifts are excluded. Ask another approver; an owner's shifts need HR approval.");
    if (shifts.length) {
      setNotes({});
      setApproval(shifts);
    }
  };
  const sendApproval = async (items: Schema["BulkApprovalItem"][]) => {
    setBusy(true);
    try {
      const response = await timesheetsApi.approve(items);
      setResult(response);
      const failures = new Set(
        response.results.filter((item) => item.status !== "approved").map((item) => item.shiftId),
      );
      setFailed(items.filter((item) => failures.has(item.shiftId)));
      setApproval(undefined);
      refresh();
    } catch {
      setError(
        "Approval could not be completed. Try again; refresh shifts before retrying after a lost connection.",
      );
    } finally {
      setBusy(false);
    }
  };
  const retryFailed = async () => {
    setBusy(true);
    try {
      const details = await Promise.all(failed.map((item) => timesheetsApi.detail(item.shiftId)));
      setNotes(Object.fromEntries(failed.map((item) => [item.shiftId, item.note ?? ""])));
      setApproval(
        details.map((detail) => ({
          id: detail.shift.id,
          version: detail.shift.version,
          status: detail.shift.status,
          flagged: detail.flags.length > 0,
        })),
      );
    } catch {
      setError("Failed shifts could not be refreshed. Try again.");
    } finally {
      setBusy(false);
    }
  };
  useEffect(() => {
    const guard = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || !(opened || driver || adding || approval)) return;
      const typing =
        event.target instanceof HTMLElement &&
        event.target.closest("input,textarea,select,[contenteditable],[role='textbox']");
      if (!shortcuts || typing) event.stopImmediatePropagation();
    };
    document.addEventListener("keydown", guard, true);
    return () => document.removeEventListener("keydown", guard, true);
  }, [opened, driver, adding, approval, shortcuts]);
  useEffect(() => {
    document.title = "Timesheets \u00B7 FleetOps Office";
  }, []);
  useEffect(() => {
    const handleKey = (event: KeyboardEvent) => {
      const target = event.target;
      if (
        !shortcuts ||
        event.defaultPrevented ||
        event.ctrlKey ||
        event.metaKey ||
        event.altKey ||
        event.isComposing ||
        (target instanceof HTMLElement &&
          target.closest("input,textarea,select,[contenteditable='true'],[role='textbox']"))
      )
        return;
      if (opened || driver || adding || approval) return;
      if (event.key === "/") {
        event.preventDefault();
        searchInput.current?.focus();
        return;
      }
      if (!grid.current?.contains(document.activeElement)) return;
      if (["j", "k"].includes(event.key)) {
        event.preventDefault();
        const next = Math.max(0, Math.min(rows.length - 1, focus + (event.key === "j" ? 1 : -1)));
        setFocus(next);
        grid.current?.querySelectorAll<HTMLButtonElement>(".ts-driver")[next]?.focus();
      }
      if (event.key === "Escape") {
        setFocus(-1);
        grid.current?.focus();
      }
      if (event.key === "Enter" && rows[focus]) {
        event.preventDefault();
        openRow(rows[focus]);
      }
      if (event.key === "a" && rows[focus] && online && !busy) {
        event.preventDefault();
        requestApproval([rows[focus]]);
      }
    };
    document.addEventListener("keydown", handleKey, true);
    return () => document.removeEventListener("keydown", handleKey, true);
  });
  const openRow = (row: Schema["WeekRow"]) => {
    if (window.matchMedia("(max-width: 767px)").matches) setDriver(row);
    else {
      const first = row.days.flatMap((day) => day.shifts)[0];
      if (first) setOpened(first.id);
      else setDriver(row);
    }
  };
  if (!allowed || (query.error instanceof AuthRequestError && query.error.status === 403))
    return (
      <div className="page">
        <h1>Timesheets</h1>
        <Panel state="denied">
          <p>
            Timesheets require dispatcher, HR or owner access. Ask an owner to grant access in Settings /
            Users & roles.
          </p>
        </Panel>
      </div>
    );
  return (
    <div className="page timesheets-page">
      <div className="page-title">
        <div>
          <span className="eyebrow">Operations / Timesheets</span>
          <h1>Timesheets</h1>
          <p className="ui-muted">Review hours, resolve warnings and close the week.</p>
        </div>
        <button
          className="ui-button primary"
          disabled={!online || !zone}
          onClick={() => setAdding({ employeeId: "", date: week })}
        >
          <Plus size={17} aria-hidden /> Add shift
        </button>
      </div>
      {!online && (
        <Banner kind="offline" label="Offline">
          Cached timesheets are read-only. Reconnect to make changes.
        </Banner>
      )}
      {query.isError && (
        <Banner kind="error" label="Error" retryLabel="Try again" onRetry={() => void query.refetch()}>
          Data could not be loaded. Check the connection and try again.
        </Banner>
      )}
      {error && (
        <Banner kind="error" label="Action needed">
          {error}
          <button className="ui-button" onClick={() => setError("")}>
            Dismiss
          </button>
        </Banner>
      )}
      {query.data?.cutoff && (
        <Banner kind="info" label="Payroll cutoff">
          Payroll cutoff {dateLabel(query.data.cutoff.date)} {"\u2014"} {query.data.cutoff.awaitingShifts}{" "}
          shifts still awaiting approval.
        </Banner>
      )}
      {result && (
        <Banner kind={result.failed ? "error" : "info"} label="Approval results">
          <p>
            Approved {result.approved}. {result.failed} could not be approved.
          </p>
          {result.results
            .filter((item) => item.status !== "approved")
            .map((item) => (
              <p key={item.shiftId}>
                {item.shiftId}:{" "}
                {item.code === "own_shift"
                  ? "Ask another approver."
                  : item.code === "owner_shift_needs_hr"
                    ? "Ask HR to approve this owner's shift."
                    : item.code?.replaceAll("_", " ")}
              </p>
            ))}
          {failed.length > 0 && (
            <button className="ui-button" disabled={busy || !online} onClick={() => void retryFailed()}>
              Retry failed
            </button>
          )}
          <button
            className="ui-button"
            onClick={() => {
              setResult(undefined);
              setFailed([]);
            }}
          >
            Dismiss
          </button>
        </Banner>
      )}
      <Panel
        title="Week review"
        filters={
          <div className="ts-toolbar">
            <button
              className="ui-button"
              aria-label="Previous week"
              onClick={() => {
                setWeek(addDays(week, -7));
                reset();
              }}
            >
              <ChevronLeft size={18} />
            </button>
            <strong>
              {dateLabel(week)} {"\u2013"} {dateLabel(addDays(week, 6))}
            </strong>
            <button
              className="ui-button"
              aria-label="Next week"
              onClick={() => {
                setWeek(addDays(week, 7));
                reset();
              }}
            >
              <ChevronRight size={18} />
            </button>
          </div>
        }
      >
        <div className="ts-toolbar">
          <div role="group" aria-label="Filter timesheets">
            {(["all", "awaiting", "missing", "warnings"] as const).map((chip) => (
              <button
                key={chip}
                className="ui-button"
                aria-pressed={filter === chip}
                onClick={() => {
                  setFilter(chip);
                  reset();
                }}
              >
                {chip[0].toUpperCase() + chip.slice(1)} {query.data?.counts[chip] ?? ""}
              </button>
            ))}
          </div>
          <label>
            Search driver
            <input
              ref={searchInput}
              maxLength={200}
              value={search}
              placeholder="Name or personnel number"
              onChange={(event) => {
                setSearch(event.target.value);
                reset();
              }}
            />
          </label>
          <label>
            <input
              type="checkbox"
              checked={shortcuts}
              onChange={(event) => {
                setShortcuts(event.target.checked);
                try {
                  localStorage.setItem("fleetops.timesheets.shortcuts", event.target.checked ? "on" : "off");
                } catch {
                  /* Browser storage unavailable. */
                }
              }}
            />{" "}
            Keyboard shortcuts
          </label>
        </div>
        <p className="ui-muted ts-help">
          {zone
            ? `Times shown in ${zone}${Intl.DateTimeFormat().resolvedOptions().timeZone !== zone ? " (company time)" : ""}.`
            : "Loading company time zone..."}{" "}
          {shortcuts && "/ Search \u00B7 j/k Move \u00B7 Enter Open \u00B7 Esc Clear \u00B7 a Approve"}
        </p>
        {query.isLoading && (
          <div role="status" aria-label="Loading timesheets">
            <Skeleton />
            <Skeleton />
            <Skeleton />
          </div>
        )}
        {query.data && rows.length === 0 && (
          <div className="ts-empty">
            <h2>No shifts yet</h2>
            <p>Drivers' shifts appear here once they start; you can also add shifts by hand.</p>
            <button
              className="ui-button primary"
              disabled={!online}
              onClick={() => setAdding({ employeeId: "", date: week })}
            >
              Add shift
            </button>
          </div>
        )}
        {rows.length > 0 && (
          <div className="ts-scroll">
            <table ref={grid} tabIndex={0} className="ts-grid" aria-label="Driver week grid">
              <thead>
                <tr>
                  <th>
                    <input
                      type="checkbox"
                      aria-label="Select this page"
                      checked={rows.length > 0 && rows.every((row) => selected.includes(row.employeeId))}
                      onChange={(event) =>
                        setSelected(event.target.checked ? rows.map((row) => row.employeeId) : [])
                      }
                    />
                  </th>
                  <th>Driver</th>
                  {["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"].map((day, index) => (
                    <th scope="col" className="ts-desktop" key={day}>
                      {day}
                      <small>{dateLabel(addDays(week, index)).slice(0, 6)}</small>
                    </th>
                  ))}
                  <th>Hours</th>
                  <th className="ts-desktop">Nights</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, index) => (
                  <tr key={row.employeeId} data-focused={index === focus}>
                    <td>
                      <input
                        type="checkbox"
                        aria-label={`Select ${row.driver}`}
                        checked={selected.includes(row.employeeId)}
                        onChange={(event) =>
                          setSelected(
                            event.target.checked
                              ? [...selected, row.employeeId]
                              : selected.filter((id) => id !== row.employeeId),
                          )
                        }
                      />
                    </td>
                    <th>
                      <button
                        className="ts-driver"
                        onFocus={() => setFocus(index)}
                        onClick={() => openRow(row)}
                      >
                        {row.driver}
                      </button>
                      <small>{row.personnelNumber}</small>
                    </th>
                    {row.days.map((day) => (
                      <td className="ts-desktop" key={day.date}>
                        <button
                          className="ts-cell"
                          aria-label={`${row.driver}, ${dateLabel(day.date)}, ${day.label}, ${hours(day.workMinutes)}`}
                          onClick={() => {
                            if (day.shifts.length === 1) setOpened(day.shifts[0].id);
                            else if (day.missing) setAdding({ employeeId: row.employeeId, date: day.date });
                            else setDriver(row);
                          }}
                        >
                          <span>
                            {day.absences.join(", ") ||
                              (day.missing
                                ? "Missing"
                                : day.shifts.length
                                  ? hours(day.workMinutes)
                                  : "\u2013")}
                          </span>
                          <ShiftStatus status={day.status} />
                          {day.shifts.length > 1 && <small>{day.shifts.length} shifts</small>}
                          {day.warnings && (
                            <span>
                              <TriangleAlert size={14} aria-hidden /> Warning
                            </span>
                          )}
                        </button>
                      </td>
                    ))}
                    <td>{hours(row.workMinutes)}</td>
                    <td className="ts-desktop">
                      {row.nights.map((night) => `${night.count} (${night.countryCode})`).join(", ") || "0"}
                    </td>
                    <td>
                      <ShiftStatus status={row.status} />
                      {row.warnings && <StatusPill status="warning" />}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {query.data && (
          <div className="ts-toolbar">
            <span>
              {query.data.total} drivers {"\u00B7"} Page {page} of{" "}
              {Math.max(1, Math.ceil(query.data.total / query.data.pageSize))}
            </span>
            <button
              className="ui-button"
              disabled={page === 1}
              onClick={() => {
                setPage(page - 1);
                setSelected([]);
                setFocus(-1);
              }}
            >
              Previous
            </button>
            <button
              className="ui-button"
              disabled={page * query.data.pageSize >= query.data.total}
              onClick={() => {
                setPage(page + 1);
                setSelected([]);
                setFocus(-1);
              }}
            >
              Next
            </button>
          </div>
        )}
      </Panel>
      {selected.length > 0 && (
        <div className="ts-selection">
          <strong>{selected.length} drivers selected</strong>
          <button
            className="ui-button primary"
            disabled={busy || !online}
            onClick={() => requestApproval(rows.filter((row) => selected.includes(row.employeeId)))}
          >
            Approve selected
          </button>
          <span>Reject and individual decisions: open a shift.</span>
          <button className="ui-button" onClick={() => setSelected([])}>
            Clear selection
          </button>
        </div>
      )}
      {driver && (
        <Drawer
          open
          title={`${driver.driver} \u00B7 ${dateLabel(week)}`}
          closeLabel="Close driver week"
          onClose={() => setDriver(undefined)}
        >
          <div className="ts-detail">
            {driver.days.map((day) => (
              <section key={day.date}>
                <h3>
                  {dateLabel(day.date)} {"\u00B7"} {hours(day.workMinutes)}
                </h3>
                <ShiftStatus status={day.status} />
                {day.absences.map((absence) => (
                  <p key={absence}>{absence}</p>
                ))}
                {day.shifts.map((shift) => (
                  <button
                    key={shift.id}
                    className="ui-button"
                    onClick={() => {
                      setOpened(shift.id);
                      setDriver(undefined);
                    }}
                  >
                    Open shift {"\u00B7"} <ShiftStatus status={shift.status} />
                  </button>
                ))}
                {day.missing && (
                  <>
                    <button
                      className="ui-button"
                      disabled={!online}
                      onClick={() => {
                        setAdding({ employeeId: driver.employeeId, date: day.date });
                        setDriver(undefined);
                      }}
                    >
                      Add shift
                    </button>
                    <a href="/people">Record absence in People</a>
                  </>
                )}
              </section>
            ))}
          </div>
        </Drawer>
      )}
      {opened && zone && (
        <ShiftDrawer
          id={opened}
          zone={zone}
          onClose={() => {
            setOpened(undefined);
            setParams(
              (previous) => {
                previous.delete("shiftId");
                return previous;
              },
              { replace: true },
            );
          }}
          onSaved={refresh}
          onApprove={(shift) => {
            setOpened(undefined);
            setApproval([shift]);
            setNotes({});
          }}
        />
      )}
      {adding && zone && (
        <Drawer
          open
          title="Add shift"
          closeLabel="Close add shift"
          onClose={() => {
            if (!busy) setAdding(undefined);
          }}
        >
          <ShiftEditor
            zone={zone}
            employeeId={adding.employeeId}
            date={adding.date}
            onSaved={() => {
              setAdding(undefined);
              refresh();
            }}
          />
        </Drawer>
      )}
      {approval && (
        <Drawer
          open
          title="Approve shifts"
          closeLabel="Close approval"
          onClose={() => {
            if (!busy) setApproval(undefined);
          }}
        >
          <form
            className="ts-detail"
            onSubmit={(event) => {
              event.preventDefault();
              void sendApproval(
                approval.map((shift) => ({
                  shiftId: shift.id,
                  version: shift.version,
                  note: notes[shift.id]?.trim() || null,
                })),
              );
            }}
          >
            <p>
              Approve {approval.length} submitted shifts. Each shift is checked separately. Own shifts cannot
              be approved; owners' shifts require HR.
            </p>
            {approval
              .filter((shift) => shift.flagged)
              .map((shift) => (
                <label key={shift.id}>
                  Warning acknowledgement for shift {shift.id}
                  <textarea
                    required
                    maxLength={500}
                    value={notes[shift.id] ?? ""}
                    onChange={(event) => setNotes({ ...notes, [shift.id]: event.target.value })}
                  />
                </label>
              ))}
            {error && <p role="alert">{error}</p>}
            <button
              className="ui-button primary"
              disabled={
                busy || !online || approval.some((shift) => shift.flagged && !notes[shift.id]?.trim())
              }
            >
              {busy ? "Approving..." : "Approve"}
            </button>
          </form>
        </Drawer>
      )}
    </div>
  );
}
