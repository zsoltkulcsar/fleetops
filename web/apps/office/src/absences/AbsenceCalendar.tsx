import type { ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Activity,
  CalendarClock,
  Clock,
  GraduationCap,
  Hourglass,
  Inbox,
  TreePalm,
  Thermometer,
  TriangleAlert,
  Undo2,
  UserX,
  Wallet,
  CircleDashed,
} from "lucide-react";
import type { components } from "@fleetops/api-client";
import { formatDocumentDate } from "../shell/date";
import { capacityCriticalAt, capacityWarnAt } from "../people/Capacity";
import { absenceRange, addDays, initials, mondayOf, shortDate, weekNumber } from "../people/directory";
import { absenceLabels, absencesApi, statusLabels, type AbsenceDetail } from "./api";

type Holiday = components["schemas"]["HolidayView"];
type AbsenceType = components["schemas"]["AbsenceType"];
export type AbsenceGroup = { name: string; items: AbsenceDetail[] };

const weekdayLetters = ["M", "T", "W", "T", "F", "S", "S"];
const typeIcons: Record<AbsenceType, ReactNode> = {
  vacation: <TreePalm size={14} aria-hidden />,
  sick: <Thermometer size={14} aria-hidden />,
  training: <GraduationCap size={14} aria-hidden />,
  unpaid: <Wallet size={14} aria-hidden />,
  time_off_in_lieu: <Clock size={14} aria-hidden />,
  other: <CircleDashed size={14} aria-hidden />,
};

/** Absences that block a day: approved or awaiting a decision. */
export function counts(item: AbsenceDetail) {
  return item.absence.status === "approved" || item.absence.status === "requested";
}

/** Mon=0 \u2026 Sun=6. */
function weekday(isoDate: string) {
  return (new Date(`${isoDate}T12:00:00Z`).getUTCDay() + 6) % 7;
}

function isWeekend(isoDate: string) {
  return weekday(isoDate) >= 5;
}

/** Driver working days (Mon\u2013Fri) in one week: approved away and requested, as days and share. */
export function weekLoad(items: AbsenceDetail[], driverIds: Set<string>, monday: string) {
  const days = [0, 1, 2, 3, 4].map((offset) => addDays(monday, offset));
  let away = 0;
  let pending = 0;
  for (const item of items) {
    if (!driverIds.has(item.absence.employeeId)) continue;
    const hit = days.filter((day) => day >= item.absence.from && day <= item.absence.to).length;
    if (item.absence.status === "approved") away += hit;
    else if (item.absence.status === "requested") pending += hit;
  }
  const total = driverIds.size * days.length;
  const share = (value: number) => (total ? Math.round((value / total) * 100) : 0);
  return { away, pending, total, awayPct: share(away), pendingPct: share(pending) };
}

function tone(awayPct: number) {
  return awayPct >= capacityCriticalAt ? "crit" : awayPct >= capacityWarnAt ? "warn" : "ok";
}

function weekdayDate(isoDate: string) {
  const name = new Intl.DateTimeFormat("en-GB", { weekday: "short", timeZone: "UTC" }).format(
    new Date(`${isoDate}T12:00:00Z`),
  );
  return `${name} ${shortDate(isoDate)}`;
}

function nextWorkingDay(isoDate: string) {
  let day = addDays(isoDate, 1);
  while (isWeekend(day)) day = addDays(day, 1);
  return day;
}

/** Stable avatar colour (0\u20133) per person, shared by the calendar and the requests list. */
export function avatarTone(id: string) {
  let sum = 0;
  for (const char of id) sum += char.charCodeAt(0);
  return sum % 4;
}

function names(list: string[]) {
  return list.length <= 3 ? list.join(", ") : `${list.slice(0, 3).join(", ")} +${list.length - 3}`;
}

export function AbsenceSummary({
  items,
  driverIds,
  driverNames,
  pending,
  today,
  canReview,
}: {
  items: AbsenceDetail[];
  driverIds: Set<string>;
  driverNames: Map<string, string>;
  pending: AbsenceDetail[];
  today: string;
  canReview: boolean;
}) {
  const ofDrivers = items.filter((item) => driverIds.has(item.absence.employeeId));
  const awayToday = ofDrivers.filter(
    (item) => item.absence.status === "approved" && item.absence.from <= today && item.absence.to >= today,
  );
  const awayNames = [...new Set(awayToday.map((item) => item.employeeName))];
  const monday = mondayOf(today);
  const week = weekLoad(items, driverIds, monday);
  const returning = [...awayToday].sort((a, b) => a.absence.to.localeCompare(b.absence.to))[0];
  const starting = ofDrivers
    .filter((item) => item.absence.status === "approved" && item.absence.from > today)
    .sort((a, b) => a.absence.from.localeCompare(b.absence.from))[0];
  const firstRequest = [...pending].sort((a, b) => a.absence.from.localeCompare(b.absence.from))[0];
  const weekTone = tone(week.awayPct);
  return (
    <section className="abs-kpis" aria-label="Absence summary">
      <div className="ui-panel abs-kpi">
        <div className="abs-kpi-head">
          Away today
          <span className="abs-kpi-icon tone-accent">
            <UserX size={16} aria-hidden />
          </span>
        </div>
        <strong>
          {awayNames.length} of {driverIds.size}
        </strong>
        <small>{awayNames.length ? names(awayNames) : "All drivers available"}</small>
      </div>
      <div className="ui-panel abs-kpi">
        <div className="abs-kpi-head">
          Requests to review
          <span className={`abs-kpi-icon ${pending.length ? "tone-warn" : "tone-ok"}`}>
            <Inbox size={16} aria-hidden />
          </span>
        </div>
        <strong>{pending.length}</strong>
        {firstRequest ? (
          canReview ? (
            <a className="abs-kpi-link" href="#absence-requests">
              {firstRequest.employeeName}, from {shortDate(firstRequest.absence.from)}
            </a>
          ) : (
            <small>
              {firstRequest.employeeName}, from {shortDate(firstRequest.absence.from)}
            </small>
          )
        ) : (
          <small>Nothing waiting</small>
        )}
      </div>
      <div className="ui-panel abs-kpi">
        <div className="abs-kpi-head">
          Capacity this week
          <span className={`abs-kpi-icon tone-${weekTone}`}>
            <Activity size={16} aria-hidden />
          </span>
        </div>
        <strong>{weekTone === "ok" ? `${100 - week.awayPct}% available` : `${week.awayPct}% away`}</strong>
        <small>
          Week {weekNumber(monday)} {"\u00b7"} {week.away} of {week.total} driver days away
          {week.pending ? `, ${week.pending} requested` : ""}
        </small>
      </div>
      <div className="ui-panel abs-kpi">
        <div className="abs-kpi-head">
          {returning ? "Next return" : "Next absence"}
          <span className="abs-kpi-icon tone-info">
            {returning ? <Undo2 size={16} aria-hidden /> : <CalendarClock size={16} aria-hidden />}
          </span>
        </div>
        {returning ? (
          <>
            <strong>{weekdayDate(nextWorkingDay(returning.absence.to))}</strong>
            <small>
              {driverNames.get(returning.absence.employeeId) ?? returning.employeeName} back from{" "}
              {absenceLabels[returning.absence.type].toLowerCase()}
            </small>
          </>
        ) : starting ? (
          <>
            <strong>{weekdayDate(starting.absence.from)}</strong>
            <small>
              {starting.employeeName}, {absenceLabels[starting.absence.type].toLowerCase()}
            </small>
          </>
        ) : (
          <>
            <strong>None planned</strong>
            <small>In the next five weeks</small>
          </>
        )}
      </div>
    </section>
  );
}

/** Stack absences of one person that overlap into separate lanes. */
function lanes(items: AbsenceDetail[]) {
  const ends: string[] = [];
  return [...items]
    .sort((a, b) => a.absence.from.localeCompare(b.absence.from))
    .map((item) => {
      let lane = ends.findIndex((end) => end < item.absence.from);
      if (lane < 0) lane = ends.length;
      ends[lane] = item.absence.to;
      return { item, lane };
    });
}

export function AbsenceCalendar({
  month,
  items,
  groups,
  driverIds,
  holidays,
  today,
  balance,
  onOpen,
}: {
  month: string;
  items: AbsenceDetail[];
  groups: [string, AbsenceGroup][];
  driverIds: Set<string>;
  holidays: Holiday[];
  today: string;
  balance?: number;
  onOpen: (item: AbsenceDetail) => void;
}) {
  const first = `${month}-01`;
  const end = new Date(`${first}T12:00:00Z`);
  end.setUTCMonth(end.getUTCMonth() + 1, 0);
  const length = end.getUTCDate();
  const last = `${month}-${String(length).padStart(2, "0")}`;
  const monthName = new Intl.DateTimeFormat("en-GB", {
    month: "long",
    year: "numeric",
    timeZone: "UTC",
  }).format(new Date(`${first}T12:00:00Z`));
  const days = Array.from({ length }, (_, index) => {
    const date = addDays(first, index);
    const holiday = holidays.find((item) => item.date === date);
    return { date, index, day: index + 1, weekend: isWeekend(date), holiday, today: date === today };
  });
  const weeks: { monday: string; start: number; span: number }[] = [];
  for (const day of days) {
    const monday = mondayOf(day.date);
    const current = weeks[weeks.length - 1];
    if (current?.monday === monday) current.span += 1;
    else weeks.push({ monday, start: day.index, span: 1 });
  }
  const columns = { gridTemplateColumns: `var(--abs-name) repeat(${length}, minmax(0, 1fr))` };
  const background = (key: string) =>
    days.map((day) => (
      <span
        key={`${key}-${day.date}`}
        className="abs-cell"
        aria-hidden="true"
        data-off={day.weekend || Boolean(day.holiday)}
        data-today={day.today}
        data-week-start={weekday(day.date) === 0 && day.index > 0}
        style={{ gridColumn: day.index + 2 }}
      />
    ));
  return (
    <div className="abs-calendar-scroll">
      <div className="abs-calendar" style={{ minWidth: `calc(var(--abs-name) + ${length * 30}px)` }}>
        <h3 className="visually-hidden">Absences in {monthName}</h3>
        <div className="abs-row abs-weeks" style={columns} aria-hidden="true">
          <span className="abs-head-label">Week</span>
          {weeks.map((week) => {
            const load = weekLoad(items, driverIds, week.monday);
            const weekTone = tone(load.awayPct);
            const wide = week.span >= 5;
            return (
              <span
                key={week.monday}
                className="abs-week"
                data-now={week.monday === mondayOf(today)}
                style={{ gridColumn: `${week.start + 2} / span ${week.span}` }}
                title={`Week ${weekNumber(week.monday)}: ${100 - load.awayPct}% available${load.pendingPct ? `, +${load.pendingPct}% requested` : ""}`}
              >
                <span className="abs-week-name">
                  {wide ? "Week " : "W"}
                  {weekNumber(week.monday)}
                  {wide && week.monday === mondayOf(today) ? ` \u00b7 now` : ""}
                </span>
                {week.span >= 3 && driverIds.size > 0 && (
                  <span className="abs-week-pills">
                    <span className={`abs-pill tone-${weekTone}`}>
                      {weekTone !== "ok" && <TriangleAlert size={11} aria-hidden />}
                      {weekTone === "ok"
                        ? `${100 - load.awayPct}%${wide ? " available" : ""}`
                        : `${load.awayPct}% away`}
                    </span>
                    {wide && load.pendingPct > 0 && (
                      <span className="abs-pill tone-pending">+{load.pendingPct}% requested</span>
                    )}
                  </span>
                )}
              </span>
            );
          })}
        </div>
        <div className="abs-row abs-days" style={columns} aria-hidden="true">
          <span className="abs-head-label">Driver</span>
          {background("days")}
          {days.map((day) => (
            <span
              key={day.date}
              className="abs-day"
              style={{ gridColumn: day.index + 2 }}
              title={day.holiday?.name}
            >
              <small>{weekdayLetters[weekday(day.date)]}</small>
              <span data-today={day.today} data-holiday={Boolean(day.holiday)}>
                {day.day}
              </span>
            </span>
          ))}
        </div>
        <ul className="abs-people" aria-label={`Absences per person, ${monthName}`}>
          {groups.map(([id, group]) => {
            const placed = lanes(
              group.items.filter(
                (item) => counts(item) && item.absence.from <= last && item.absence.to >= first,
              ),
            );
            const laneCount = Math.max(1, ...placed.map((entry) => entry.lane + 1));
            return (
              <li
                key={id}
                className="abs-row abs-person-row"
                style={{ ...columns, gridTemplateRows: `repeat(${laneCount}, 40px)` }}
              >
                <div className="abs-person" style={{ gridRow: `1 / span ${laneCount}` }}>
                  <span className="abs-avatar" data-tone={avatarTone(id)} aria-hidden="true">
                    {initials(group.name)}
                  </span>
                  <span className="abs-person-text">
                    <strong>{group.name}</strong>
                    {balance !== undefined && <PersonDaysLeft employeeId={id} year={balance} />}
                  </span>
                </div>
                {days.map((day) => (
                  <span
                    key={day.date}
                    className="abs-cell"
                    aria-hidden="true"
                    data-off={day.weekend || Boolean(day.holiday)}
                    data-today={day.today}
                    data-week-start={weekday(day.date) === 0 && day.index > 0}
                    style={{ gridColumn: day.index + 2, gridRow: `1 / span ${laneCount}` }}
                  />
                ))}
                {!placed.length && <span className="visually-hidden">No absences</span>}
                {placed.map(({ item, lane }) => {
                  const from = item.absence.from < first ? first : item.absence.from;
                  const to = item.absence.to > last ? last : item.absence.to;
                  const start = Number(from.slice(8, 10));
                  const span = Number(to.slice(8, 10)) - start + 1;
                  const requested = item.absence.status === "requested";
                  const half = item.absence.halfDay
                    ? ` (${item.absence.halfDay === "am" ? "Morning" : "Afternoon"})`
                    : "";
                  return (
                    <button
                      key={item.absence.id}
                      type="button"
                      className="abs-bar"
                      data-type={item.absence.type}
                      data-status={item.absence.status}
                      data-clipped-start={item.absence.from < first}
                      data-clipped-end={item.absence.to > last}
                      style={{ gridColumn: `${start + 1} / span ${span}`, gridRow: lane + 1 }}
                      onClick={() => onOpen(item)}
                      aria-label={`${group.name}, ${absenceLabels[item.absence.type]}, ${formatDocumentDate(item.absence.from)} to ${formatDocumentDate(item.absence.to)}${half}, ${statusLabels[item.absence.status]}`}
                      title={`${absenceLabels[item.absence.type]}, ${absenceRange(item.absence.from, item.absence.to)}${half}, ${statusLabels[item.absence.status]}`}
                    >
                      {requested ? <Hourglass size={14} aria-hidden /> : typeIcons[item.absence.type]}
                      <span className="abs-bar-type">{absenceLabels[item.absence.type]}</span>
                      <span className="abs-bar-meta">
                        {requested
                          ? `requested \u00b7 ${absenceRange(item.absence.from, item.absence.to)}`
                          : `${absenceRange(item.absence.from, item.absence.to)}${half}${
                              item.workingDays
                                ? ` \u00b7 ${item.workingDays} working day${item.workingDays === 1 ? "" : "s"}`
                                : ""
                            }`}
                      </span>
                    </button>
                  );
                })}
              </li>
            );
          })}
        </ul>
        {driverIds.size > 0 && (
          <div className="abs-row abs-available" style={columns}>
            <span className="abs-head-label abs-available-label">Drivers available</span>
            {background("available")}
            {days.map((day) => {
              if (day.weekend || day.holiday) return null;
              const gone = new Set(
                items
                  .filter(
                    (item) =>
                      item.absence.status === "approved" &&
                      driverIds.has(item.absence.employeeId) &&
                      item.absence.from <= day.date &&
                      item.absence.to >= day.date,
                  )
                  .map((item) => item.absence.employeeId),
              ).size;
              const dayTone = tone(Math.round((gone / driverIds.size) * 100));
              return (
                <span key={day.date} className="abs-avail" style={{ gridColumn: day.index + 2 }}>
                  <span data-tone={dayTone} title={`${driverIds.size - gone} of ${driverIds.size} available`}>
                    {driverIds.size - gone}
                    <span className="visually-hidden">
                      {" "}
                      of {driverIds.size} drivers available on {formatDocumentDate(day.date)}
                    </span>
                  </span>
                </span>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}

export function AbsenceLegend({ note }: { note: string }) {
  return (
    <div className="abs-legend">
      <ul aria-label="Legend">
        <li data-type="vacation">Vacation</li>
        <li data-type="sick">Sick</li>
        <li data-type="training">Training</li>
        <li data-type="other">Other leave</li>
        <li data-type="requested">Requested, not yet approved</li>
        <li data-type="off">Weekend or holiday</li>
      </ul>
      <span>{note}</span>
    </div>
  );
}

function PersonDaysLeft({ employeeId, year }: { employeeId: string; year: number }) {
  const balance = useQuery({
    queryKey: ["absences", "balance", employeeId, year],
    queryFn: () => absencesApi.balance(employeeId, year),
  });
  return (
    <small>
      {balance.data ? `${balance.data.left} days left` : balance.isError ? "Balance unavailable" : "\u2026"}
    </small>
  );
}
