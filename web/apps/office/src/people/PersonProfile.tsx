import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams, useSearchParams } from "react-router";
import { Banner, StatusPill } from "@fleetops/ui";
import { useSession } from "../auth/AuthGate";
import { useOnline } from "../shell/Shell";
import { formatBerlinDateTime, formatDocumentDate } from "../shell/date";
import { peopleApi } from "./api";
import { peopleErrorText } from "./errors";
import { documentLabel, personRoles, stateLabels } from "./labels";
import { CanDrive, PersonStatus } from "./PersonStatus";
import { PersonForm } from "./PersonForm";
import { PersonActionDialog, type PersonAction } from "./PersonActionDialog";
import { SendEnrolment } from "./SendEnrolment";
import "../fleet/fleet.css";
import "./people.css";

export function PersonProfile() {
  const { id = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const session = useSession();
  const online = useOnline();
  const client = useQueryClient();
  const canManage = session.data?.roles.some((role) => ["hr", "owner"].includes(role)) === true;
  const canAccess = session.data?.roles.some((role) => ["hr", "owner", "dispatcher"].includes(role)) === true;
  const [tab, setTab] = useState("documents");
  const [editing, setEditing] = useState(false);
  const [action, setAction] = useState<PersonAction | null>(null);
  const [sessionId, setSessionId] = useState<string>();
  const [saved, setSaved] = useState("");
  const person = useQuery({ queryKey: ["person", id], queryFn: () => peopleApi.detail(id) });
  const sessions = useQuery({
    queryKey: ["person-sessions", id],
    queryFn: () => peopleApi.sessions(id),
    enabled: tab === "access" && canAccess,
  });
  useEffect(() => {
    document.title = `${person.data?.header.name ?? "Person"} \u00b7 FleetOps`;
  }, [person.data?.header.name]);
  useEffect(() => {
    if (params.get("action") === "suspend" && canManage && person.data?.header.status === "active") {
      setAction("suspend");
      const next = new URLSearchParams(params);
      next.delete("action");
      setParams(next, { replace: true });
    }
  }, [params, canManage, person.data, setParams]);
  async function refresh(message: string) {
    setSaved(message);
    await Promise.all([
      client.invalidateQueries({ queryKey: ["person", id] }),
      client.invalidateQueries({ queryKey: ["people"] }),
      client.invalidateQueries({ queryKey: ["person-sessions", id] }),
      client.invalidateQueries({ queryKey: ["attention"] }),
    ]);
  }
  if (person.isLoading)
    return (
      <div className="page">
        <h1 tabIndex={-1}>Person</h1>
        <p role="status">{"Loading profile\u2026"}</p>
      </div>
    );
  if (!person.data)
    return (
      <div className="page">
        <h1 tabIndex={-1}>Person</h1>
        <Banner
          kind="error"
          label="Profile unavailable"
          retryLabel="Try again"
          onRetry={() => void person.refetch()}
        >
          {peopleErrorText(person.error)}
        </Banner>
        <Link to="/people">Back to People</Link>
      </div>
    );
  const data = person.data;
  const button = (value: PersonAction, label: string) => (
    <button type="button" className="ui-button" disabled={!online} onClick={() => setAction(value)}>
      {label}
    </button>
  );
  return (
    <div className="page directory-page people-profile">
      <Link to="/people">Back to People</Link>
      <header className="page-title">
        <div>
          <h1 tabIndex={-1}>{data.header.name}</h1>
          <div className="people-profile-summary">
            <span>{personRoles(data.header.roles)}</span>
            <PersonStatus status={data.header.status} />
            <span>
              Can drive <CanDrive person={data.header} />
            </span>
          </div>
        </div>
        <div className="people-actions">
          {canManage && (
            <>
              <button className="ui-button" type="button" disabled={!online} onClick={() => setEditing(true)}>
                Edit fields
              </button>
              {data.header.status === "active" && button("suspend", "Suspend")}
              {data.header.status === "suspended" && button("reactivate", "Reactivate")}
              {data.header.status === "left" ? button("rehire", "Rehire") : button("leave", "Mark as left")}
            </>
          )}
        </div>
      </header>
      {!online && <Banner label="Offline">Cached profile is read-only until the connection returns.</Banner>}
      {person.isError && (
        <Banner
          kind="error"
          label="Profile could not be refreshed"
          retryLabel="Try again"
          onRetry={() => void person.refetch()}
        >
          Showing the last loaded profile.
        </Banner>
      )}
      {saved && <Banner label="Saved">{saved}</Banner>}
      <div className="directory-tabs" role="tablist" aria-label="Person profile">
        {["documents", "access", "history"].map((value) => (
          <button
            key={value}
            type="button"
            role="tab"
            id={`person-tab-${value}`}
            aria-controls="person-tab-panel"
            aria-selected={tab === value}
            onClick={() => setTab(value)}
          >
            {value === "documents" ? "Documents" : value === "access" ? "Access" : "History"}
          </button>
        ))}
      </div>
      <section
        id="person-tab-panel"
        role="tabpanel"
        aria-labelledby={`person-tab-${tab}`}
        className="ui-panel people-profile-panel"
      >
        {tab === "documents" && (
          <>
            <div className="page-title">
              <div>
                <h2>{data.header.status === "onboarding" ? "Onboarding checklist" : "Documents"}</h2>
                <p>
                  {data.verifiedCount} of {data.requiredCount} verified
                </p>
              </div>
              {canManage && data.header.status === "onboarding" && button("activate", "Activate")}
            </div>
            <div className="table-wrap">
              <table>
                <caption className="visually-hidden">Required documents</caption>
                <thead>
                  <tr>
                    <th>Document</th>
                    <th>State</th>
                    <th>Valid until</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {data.checklist.map((item) => (
                    <tr key={item.type}>
                      <td>{documentLabel(item.type)}</td>
                      {item.masked || (!canManage && item.type === "medical") ? (
                        <td colSpan={3}>{"Medical \u2014 visible to HR only"}</td>
                      ) : (
                        <>
                          <td>
                            <StatusPill
                              status={
                                item.state === "verified"
                                  ? "verified"
                                  : item.state === "expiring" || item.state === "uploaded"
                                    ? "warning"
                                    : item.state === "expired" || item.state === "rejected"
                                      ? "blocked"
                                      : "missing"
                              }
                              label={stateLabels[item.state ?? "missing"] ?? "Unknown state"}
                            />
                          </td>
                          <td>{formatDocumentDate(item.expiresAt)}</td>
                          <td>
                            {canManage && (
                              <Link
                                className="ui-button"
                                to={`/documents?upload=employee:${id}:${item.type}`}
                              >
                                {item.state === "missing" ? "Upload document" : "Upload renewal"}
                              </Link>
                            )}
                            {canManage && item.state === "uploaded" && (
                              <Link className="ui-button" to="/documents?filter=queue">
                                Review document
                              </Link>
                            )}
                          </td>
                        </>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
        {tab === "access" && (
          <>
            <h2>Access</h2>
            {canAccess && (
              <div className="people-actions">
                {online && <SendEnrolment id={id} name={data.header.name} label="Resend enrolment" />}
                {button("resetPin", "Reset PIN")}
                {canManage && button("phone", "Change number")}
              </div>
            )}
            {sessions.isLoading && <p role="status">{"Loading sessions\u2026"}</p>}
            {sessions.isError && (
              <Banner
                kind="error"
                label="Sessions could not be loaded"
                retryLabel="Try again"
                onRetry={() => void sessions.refetch()}
              >
                Check the connection and try again.
              </Banner>
            )}
            {sessions.data?.items.length === 0 && <p>No sessions.</p>}
            {sessions.data?.items.map((entry) => (
              <article key={entry.id} className="people-session">
                <div>
                  <h3>{entry.device}</h3>
                  <p>Created {formatBerlinDateTime(entry.createdAt)}</p>
                  <p>Last seen {formatBerlinDateTime(entry.lastSeenAt)}</p>
                  <p>
                    {entry.status === "active"
                      ? "Active"
                      : entry.status === "revoked"
                        ? "Revoked"
                        : "Inactive"}
                  </p>
                  {entry.pinResetRequestedAt && (
                    <p>PIN reset requested {formatBerlinDateTime(entry.pinResetRequestedAt)}</p>
                  )}
                </div>
                {canAccess && entry.status === "active" && (
                  <button
                    type="button"
                    className="ui-button"
                    disabled={!online}
                    onClick={() => {
                      setSessionId(entry.id);
                      setAction("revoke");
                    }}
                  >
                    Revoke
                  </button>
                )}
              </article>
            ))}
          </>
        )}
        {tab === "history" && (
          <>
            <h2>History</h2>
            <p>Audit history is currently unavailable for this profile.</p>
          </>
        )}
      </section>
      {editing && (
        <PersonForm
          person={data}
          onClose={() => setEditing(false)}
          onSaved={() => {
            setEditing(false);
            void refresh("Person details updated.");
          }}
        />
      )}
      {action && (
        <PersonActionDialog
          person={data}
          action={action}
          sessionId={sessionId}
          onClose={() => setAction(null)}
          onSaved={() => {
            setAction(null);
            setSessionId(undefined);
            void refresh("Change saved.");
          }}
        />
      )}
    </div>
  );
}
