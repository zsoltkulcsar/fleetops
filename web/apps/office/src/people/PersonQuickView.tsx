import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import { Banner, Drawer, Plate, StatusPill } from "@fleetops/ui";
import { formatDocumentDate } from "../shell/date";
import { absenceLabels, statusLabels as absenceStatusLabels } from "../absences/api";
import { peopleApi, type PersonRow } from "./api";
import { peopleErrorText } from "./errors";
import { documentLabel, personRoles, stateLabels } from "./labels";
import { CanDrive, PersonStatus } from "./PersonStatus";
import { ContactActions } from "./ContactActions";
import {
  absenceRange,
  absenceWindowDays,
  daysBetween,
  documentPill,
  employmentLabels,
  initials,
  languageLabels,
  type AbsenceItem,
} from "./directory";

export type QuickViewTab = "overview" | "documents" | "absences";

const tabLabels: Record<QuickViewTab, string> = {
  overview: "Overview",
  documents: "Documents",
  absences: "Absences",
};

function expiryNote(expiresAt: string | null, today: string) {
  if (!expiresAt) return null;
  const days = daysBetween(today, expiresAt);
  if (days < 0) return `Expired ${-days} ${-days === 1 ? "day" : "days"} ago`;
  if (days === 0) return "Expires today";
  if (days <= 60) return `Expires in ${days} ${days === 1 ? "day" : "days"}`;
  return null;
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
  const [focusTab, setFocusTab] = useState(false);
  const tabs: QuickViewTab[] = ["overview", "documents", "absences"];
  const isDriver = person.roles.includes("driver");
  const fields = detail.data?.fields;
  const fact = (label: string, value: string | null | undefined) => (
    <div>
      <dt>{label}</dt>
      <dd>{value || "Not recorded"}</dd>
    </div>
  );
  return (
    <Drawer open title={person.name} closeLabel="Close" onClose={onClose}>
      <div className="people-quick">
        <div className="people-quick-head">
          <span className="people-avatar large" aria-hidden>
            {initials(person.name)}
          </span>
          <div>
            <p className="people-quick-roles">{personRoles(person.roles)}</p>
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
        {person.phone && (
          <div className="people-quick-contact">
            <span className="ui-mono">{person.phone}</span>
            <ContactActions name={person.name} phone={person.phone} />
          </div>
        )}
        <div className="people-quick-tabs" role="tablist" aria-label={`${person.name} quick view`}>
          {tabs.map((value) => (
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
                const step = event.key === "ArrowRight" ? 1 : tabs.length - 1;
                onTab(tabs[(tabs.indexOf(tab) + step) % tabs.length]);
                setFocusTab(true);
              }}
            >
              {tabLabels[value]}
              {value === "documents" && person.worstState && person.worstState !== "verified" && (
                <>
                  <span className="people-tab-dot" aria-hidden />
                  <span className="visually-hidden">, needs attention</span>
                </>
              )}
              {value === "absences" && absences.some((item) => item.absence.status === "requested") && (
                <>
                  <span className="people-tab-dot" aria-hidden />
                  <span className="visually-hidden">, request awaiting a decision</span>
                </>
              )}
            </button>
          ))}
        </div>
        <section id="quick-tab-panel" role="tabpanel" aria-labelledby={`quick-tab-${tab}`}>
          {tab === "overview" && (
            <>
              {isDriver && (
                <div className="people-quick-card">
                  <h3 className="eyebrow">Default vehicle</h3>
                  {person.defaultVehiclePlate ? (
                    <div className="people-quick-vehicle">
                      <Plate>{person.defaultVehiclePlate}</Plate>
                      <Link to="/plan">Change in Plan</Link>
                    </div>
                  ) : (
                    <div className="people-quick-vehicle">
                      <span>No truck assigned.</span>
                      <Link to="/plan">Assign in Plan</Link>
                    </div>
                  )}
                </div>
              )}
              <div className="people-quick-card">
                <h3 className="eyebrow">Employment</h3>
                {detail.isLoading && <p role="status">{"Loading details\u2026"}</p>}
                {detail.isError && (
                  <Banner
                    kind="error"
                    label="Details unavailable"
                    retryLabel="Try again"
                    onRetry={() => void detail.refetch()}
                  >
                    {peopleErrorText(detail.error)}
                  </Banner>
                )}
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
                    {canManage &&
                      fields.vacationEntitlementDays != null &&
                      fact("Vacation entitlement", `${fields.vacationEntitlementDays} days per year`)}
                  </dl>
                )}
              </div>
            </>
          )}
          {tab === "documents" && (
            <>
              <p className="people-quick-lead">
                {detail.data
                  ? `${detail.data.verifiedCount} of ${detail.data.requiredCount} required documents verified.`
                  : `${person.validDocuments} of ${person.requiredDocuments} valid.`}
              </p>
              {detail.isLoading && <p role="status">{"Loading documents\u2026"}</p>}
              {detail.isError && (
                <Banner
                  kind="error"
                  label="Documents unavailable"
                  retryLabel="Try again"
                  onRetry={() => void detail.refetch()}
                >
                  {peopleErrorText(detail.error)}
                </Banner>
              )}
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
          {tab === "absences" && (
            <>
              <p className="people-quick-lead">
                Approved and requested absences in the next {absenceWindowDays} days.
              </p>
              {absencesFailed ? (
                <p>Absences could not be loaded.</p>
              ) : absences.length === 0 ? (
                <p>No absences planned.</p>
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
              <Link to={`/people/${person.id}?tab=absences`}>All absences and vacation balance</Link>
            </>
          )}
        </section>
        <footer className="people-quick-footer">
          <Link className="ui-button primary" to={`/people/${person.id}`}>
            Open full profile
          </Link>
        </footer>
      </div>
    </Drawer>
  );
}
