import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";
import { CalendarDays, CreditCard, FileText, HeartPulse, Phone, Send, Truck, Wallet } from "lucide-react";
import { Banner, Drawer, Plate, StatusPill } from "@fleetops/ui";
import { formatDocumentDate } from "../shell/date";
import { ComingLater } from "../shell/ComingLater";
import { useOnline } from "../shell/Shell";
import {
  AbsenceError,
  absenceLabels,
  absencesApi,
  statusLabels as absenceStatusLabels,
} from "../absences/api";
import { timesheetsApi } from "../timesheets/api";
import { hours } from "../timesheets/presentation";
import { peopleApi, type PersonRow } from "./api";
import { peopleErrorText } from "./errors";
import { documentLabel, roleLabels, stateLabels } from "./labels";
import { CanDrive, PersonStatus } from "./PersonStatus";
import { ContactActions } from "./ContactActions";
import {
  absenceRange,
  daysBetween,
  documentPill,
  employmentLabels,
  initials,
  languageLabels,
  mondayOf,
  weekNumber,
  type AbsenceItem,
} from "./directory";

export type QuickViewTab = "overview" | "documents" | "leave" | "finance";

const tabs: { value: QuickViewTab; label: string; icon: typeof Truck }[] = [
  { value: "overview", label: "Overview", icon: Truck },
  { value: "documents", label: "Documents", icon: FileText },
  { value: "leave", label: "Leave & hours", icon: CalendarDays },
  { value: "finance", label: "Cards & per diem", icon: Wallet },
];

const weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

function expiryNote(expiresAt: string | null, today: string) {
  if (!expiresAt) return null;
  const days = daysBetween(today, expiresAt);
  if (days < 0) return `Expired ${-days} ${-days === 1 ? "day" : "days"} ago`;
  if (days === 0) return "Expires today";
  if (days <= 60) return `Expires in ${days} ${days === 1 ? "day" : "days"}`;
  return `${days} days left`;
}

function LeaveRequest({ item }: { item: AbsenceItem }) {
  const client = useQueryClient();
  const online = useOnline();
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const approve = async () => {
    setBusy(true);
    setMessage("");
    try {
      await absencesApi.decide(item.absence.id, { approve: true, reason: null, confirmOverlap: false });
      await Promise.all([
        client.invalidateQueries({ queryKey: ["absences"] }),
        client.invalidateQueries({ queryKey: ["attention"] }),
        client.invalidateQueries({ queryKey: ["schedule"] }),
      ]);
    } catch (failure) {
      setMessage(
        failure instanceof AbsenceError && failure.code === "absence_overlaps_shifts"
          ? "This request overlaps recorded shifts. Review it in Absences."
          : "The request could not be approved. Try again or review it in Absences.",
      );
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="people-request">
      <div className="people-request-head">
        <span className="eyebrow">Leave request</span>
        <span className="ui-muted">Sent from the driver app</span>
      </div>
      <strong>
        {absenceRange(item.absence.from, item.absence.to)} {"\u00b7"} {absenceLabels[item.absence.type]}
      </strong>
      <span className="ui-muted">
        {item.workingDays} {item.workingDays === 1 ? "working day" : "working days"}
        {item.othersAbsentCount > 0 && ` \u00b7 ${item.othersAbsentCount} others away on these dates`}
      </span>
      {item.absence.note && <span>{item.absence.note}</span>}
      {message && <p role="alert">{message}</p>}
      <div className="people-request-actions">
        <button
          type="button"
          className="ui-button primary"
          disabled={busy || !online}
          onClick={() => void approve()}
        >
          {busy ? "Approving\u2026" : "Approve request"}
        </button>
        <Link className="ui-button" to="/people/absences">
          Decline in Absences
        </Link>
      </div>
    </div>
  );
}

export function PersonQuickView({
  person,
  tab,
  onTab,
  onClose,
  absences,
  absencesFailed,
  today,
  canManage,
}: {
  person: PersonRow;
  tab: QuickViewTab;
  onTab: (tab: QuickViewTab) => void;
  onClose: () => void;
  absences: AbsenceItem[];
  absencesFailed: boolean;
  today: string;
  canManage: boolean;
}) {
  const detail = useQuery({ queryKey: ["person", person.id], queryFn: () => peopleApi.detail(person.id) });
  const isDriver = person.roles.includes("driver");
  const year = Number(today.slice(0, 4));
  const monday = mondayOf(today);
  const balance = useQuery({
    queryKey: ["vacation-balance", person.id, year],
    queryFn: () => absencesApi.balance(person.id, year),
    enabled: tab === "leave",
    retry: false,
  });
  const week = useQuery({
    queryKey: ["timesheets", monday, "all", person.name, 1],
    queryFn: () => timesheetsApi.week(monday, "all", person.name, 1),
    enabled: tab === "leave" && isDriver,
    retry: false,
  });
  const weekRow = week.data?.rows.find((row) => row.employeeId === person.id);
  const [focusTab, setFocusTab] = useState(false);
  const fields = detail.data?.fields;
  const fact = (label: string, value: string | null | undefined) => (
    <div>
      <dt>{label}</dt>
      <dd>{value || "Not recorded"}</dd>
    </div>
  );
  const requests = absences.filter((item) => item.absence.status === "requested");
  return (
    <Drawer open title={person.name} closeLabel="Close" onClose={onClose}>
      <div className="people-quick">
        <div className="people-quick-head">
          <span className="people-avatar large" aria-hidden>
            {initials(person.name)}
          </span>
          <div>
            <div className="people-role-badges">
              {person.roles.map((role) => (
                <span key={role} className={`people-role-badge role-${role}`}>
                  {roleLabels[role] ?? "Other role"}
                </span>
              ))}
            </div>
            <div className="people-quick-pills">
              <PersonStatus status={person.status} />
              {isDriver && (
                <span className="people-quick-candrive">
                  Can drive <CanDrive person={person} />
                </span>
              )}
            </div>
          </div>
        </div>
        <div className="people-quick-contact">
          <Phone size={15} aria-hidden />
          {person.phone ? (
            <>
              <span className="ui-mono">{person.phone}</span>
              <ContactActions name={person.name} phone={person.phone} />
            </>
          ) : (
            <span className="ui-muted">No phone recorded</span>
          )}
          <span className="people-quick-contact-later">
            <ComingLater compact title="Extension" />
            <ComingLater compact title="Depot" />
          </span>
        </div>
        <div className="people-quick-tabs" role="tablist" aria-label={`${person.name} details`}>
          {tabs.map(({ value, label, icon: Icon }) => (
            <button
              key={value}
              type="button"
              role="tab"
              id={`quick-tab-${value}`}
              aria-controls="quick-tab-panel"
              aria-selected={tab === value}
              tabIndex={tab === value ? 0 : -1}
              ref={(element) => {
                if (element && focusTab && tab === value) {
                  element.focus();
                  setFocusTab(false);
                }
              }}
              onClick={() => onTab(value)}
              onKeyDown={(event) => {
                if (event.key !== "ArrowRight" && event.key !== "ArrowLeft") return;
                const index = tabs.findIndex((item) => item.value === tab);
                const step = event.key === "ArrowRight" ? 1 : tabs.length - 1;
                onTab(tabs[(index + step) % tabs.length].value);
                setFocusTab(true);
              }}
            >
              <Icon size={15} aria-hidden />
              {label}
              {value === "documents" && person.worstState && person.worstState !== "verified" && (
                <>
                  <span className="people-tab-dot" aria-hidden />
                  <span className="visually-hidden">, needs attention</span>
                </>
              )}
              {value === "leave" && requests.length > 0 && (
                <>
                  <span className="people-tab-dot" aria-hidden />
                  <span className="visually-hidden">, request awaiting a decision</span>
                </>
              )}
            </button>
          ))}
        </div>
        <section id="quick-tab-panel" role="tabpanel" aria-labelledby={`quick-tab-${tab}`}>
          {detail.isError && tab !== "finance" && (
            <Banner
              kind="error"
              label="Details unavailable"
              retryLabel="Try again"
              onRetry={() => void detail.refetch()}
            >
              {peopleErrorText(detail.error)}
            </Banner>
          )}
          {tab === "overview" && (
            <>
              {isDriver && (
                <div className="people-quick-card">
                  <div className="people-quick-card-head">
                    <h3 className="eyebrow">
                      <Truck size={14} aria-hidden /> Default vehicle
                    </h3>
                    <Link to="/plan">{person.defaultVehiclePlate ? "Change in Plan" : "Assign in Plan"}</Link>
                  </div>
                  {person.defaultVehiclePlate ? (
                    <div className="people-quick-vehicle">
                      <Plate>{person.defaultVehiclePlate}</Plate>
                      <span className="people-active-tag">Default truck</span>
                    </div>
                  ) : (
                    <p className="ui-muted">No truck assigned to this driver.</p>
                  )}
                </div>
              )}
              <div className="people-quick-grid">
                <div className="people-quick-card">
                  <h3 className="eyebrow">Employment</h3>
                  {detail.isLoading && <p role="status">{"Loading details\u2026"}</p>}
                  {fields && (
                    <dl className="people-facts">
                      {fact("Personnel number", fields.personnelNumber)}
                      {fact("Start date", formatDocumentDate(fields.startDate))}
                      {fact(
                        "Employment",
                        fields.employmentType ? (employmentLabels[fields.employmentType] ?? "Other") : null,
                      )}
                      {fact("Language", fields.languagePref ? languageLabels[fields.languagePref] : null)}
                      {fact("Email", fields.email)}
                    </dl>
                  )}
                </div>
                <ComingLater title="Emergency contact">
                  <p>
                    <HeartPulse size={14} aria-hidden /> Who to call after an accident on the road. Not stored
                    yet.
                  </p>
                </ComingLater>
              </div>
            </>
          )}
          {tab === "documents" && (
            <>
              <div className="people-quick-section-head">
                <div>
                  <h3>Licences, cards and expiry</h3>
                  <p className="people-quick-lead">
                    {detail.data
                      ? `${detail.data.verifiedCount} of ${detail.data.requiredCount} required documents verified.`
                      : `${person.validDocuments} of ${person.requiredDocuments} valid.`}
                  </p>
                </div>
              </div>
              {detail.isLoading && <p role="status">{"Loading documents\u2026"}</p>}
              <ul className="people-quick-list">
                {detail.data?.checklist.map((item) => {
                  const masked = item.masked || (!canManage && item.type === "medical");
                  const note = masked ? null : expiryNote(item.expiresAt, today);
                  return (
                    <li
                      key={item.type}
                      className={`people-doc tone-${masked ? "missing" : documentPill(item.state)}`}
                    >
                      <div>
                        <strong>{documentLabel(item.type)}</strong>
                        {masked ? (
                          <span>{"Medical \u2014 visible to HR only"}</span>
                        ) : (
                          <span>
                            Valid until {formatDocumentDate(item.expiresAt)}
                            {note && ` \u00b7 ${note}`}
                          </span>
                        )}
                      </div>
                      {!masked && (
                        <div className="people-doc-actions">
                          <StatusPill
                            status={documentPill(item.state)}
                            label={stateLabels[item.state ?? "missing"] ?? "Unknown state"}
                          />
                          {canManage && item.state === "uploaded" && (
                            <Link className="ui-button" to="/documents?filter=queue">
                              Review
                            </Link>
                          )}
                          {canManage && item.state !== "uploaded" && item.state !== "verified" && (
                            <Link
                              className="ui-button"
                              to={`/documents?upload=employee:${person.id}:${item.type}`}
                            >
                              {item.state === "missing" ? "Upload" : "Upload renewal"}
                            </Link>
                          )}
                        </div>
                      )}
                    </li>
                  );
                })}
              </ul>
            </>
          )}
          {tab === "leave" && (
            <>
              <div className="people-balance" role="group" aria-label={`Vacation ${year}`}>
                {balance.data ? (
                  <>
                    <div>
                      <span>Entitlement {year}</span>
                      <strong className="tabular-nums">
                        {balance.data.entitlement + balance.data.opening} days
                      </strong>
                    </div>
                    <div className="tone-used">
                      <span>Taken or planned</span>
                      <strong className="tabular-nums">{balance.data.used} days</strong>
                    </div>
                    <div className="tone-left">
                      <span>Left</span>
                      <strong className="tabular-nums">{balance.data.remaining} days</strong>
                    </div>
                  </>
                ) : (
                  <p className="ui-muted">
                    {balance.isLoading ? "Loading vacation balance\u2026" : "Vacation balance unavailable."}
                  </p>
                )}
              </div>
              {requests.map((item) => (
                <LeaveRequest key={item.absence.id} item={item} />
              ))}
              {isDriver && (
                <div className="people-quick-card">
                  <div className="people-quick-card-head">
                    <h3 className="eyebrow">Timesheet, week {weekNumber(monday)}</h3>
                    {weekRow && (
                      <strong className="tabular-nums">{hours(weekRow.workMinutes)} h worked</strong>
                    )}
                  </div>
                  {weekRow ? (
                    <div className="people-week">
                      {weekRow.days.map((day, index) => (
                        <Link
                          key={day.date}
                          to={`/timesheets?week=${monday}&search=${encodeURIComponent(person.name)}`}
                          className={`people-week-day day-${day.absences.length ? "absence" : day.missing ? "missing" : day.status}`}
                          aria-label={`${weekdays[index]}: ${day.absences.join(", ") || (day.missing ? "missing" : hours(day.workMinutes))}`}
                        >
                          <span>{weekdays[index]}</span>
                          <strong>
                            {day.absences.length
                              ? "Away"
                              : day.missing
                                ? "Missing"
                                : day.workMinutes
                                  ? hours(day.workMinutes)
                                  : "\u2013"}
                          </strong>
                        </Link>
                      ))}
                    </div>
                  ) : (
                    <p className="ui-muted">
                      {week.isLoading ? "Loading this week\u2026" : "No shifts recorded this week."}
                    </p>
                  )}
                  <ComingLater compact title="Driving time from tachograph" />
                </div>
              )}
              <div className="people-quick-card">
                <h3 className="eyebrow">Upcoming absences</h3>
                {absencesFailed ? (
                  <p>Absences could not be loaded.</p>
                ) : absences.length === 0 ? (
                  <p className="ui-muted">No absences planned in the next five weeks.</p>
                ) : (
                  <ul className="people-quick-list">
                    {absences.map((item) => (
                      <li
                        key={item.absence.id}
                        className={`people-doc tone-${item.absence.status === "requested" ? "warning" : "idle"}`}
                      >
                        <div>
                          <strong>{absenceRange(item.absence.from, item.absence.to)}</strong>
                          <span>
                            {absenceLabels[item.absence.type]} {"\u00b7"} {item.workingDays}{" "}
                            {item.workingDays === 1 ? "working day" : "working days"}
                          </span>
                        </div>
                        <StatusPill
                          status={item.absence.status === "requested" ? "submitted" : "absence"}
                          label={absenceStatusLabels[item.absence.status]}
                        />
                      </li>
                    ))}
                  </ul>
                )}
                <Link to={`/people/${person.id}?tab=absences`}>All absences and vacation history</Link>
              </div>
            </>
          )}
          {tab === "finance" && (
            <>
              <ComingLater title="Fuel card">
                <p>
                  <CreditCard size={14} aria-hidden /> Card provider and number. PINs will never be shown in
                  full.
                </p>
              </ComingLater>
              <ComingLater title="Toll devices">
                <p>Toll boxes and on-board units used by this person.</p>
              </ComingLater>
              <ComingLater title="Per diem this week">
                <p>Meal allowance per trip day. Rates to confirm with payroll; no amounts are guessed.</p>
              </ComingLater>
            </>
          )}
        </section>
        <footer className="people-quick-footer">
          <button type="button" className="ui-button" onClick={onClose}>
            Close
          </button>
          <span className="people-quick-footer-actions">
            <button type="button" className="ui-button" disabled title="Coming in Epic 15">
              <Send size={15} aria-hidden /> Message to driver app
            </button>
            <Link className="ui-button primary" to={`/people/${person.id}`}>
              Open full profile
            </Link>
          </span>
        </footer>
      </div>
    </Drawer>
  );
}
