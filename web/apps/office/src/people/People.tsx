import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { EmptyState, Panel, StatusPill } from "@fleetops/ui";
import { readinessApi } from "../readiness/api";
import { peopleCauseText } from "../readiness/labels";
import { en } from "../i18n/en";
import { useSession } from "../auth/AuthGate";
import { SendEnrolment } from "./SendEnrolment";

export function People() {
  const session = useSession();
  const canSendEnrolment =
    session.data?.roles.some((role) => ["hr", "dispatcher", "owner"].includes(role)) === true;
  const [blockedOnly, setBlockedOnly] = useState(false);
  useEffect(() => {
    document.title = `People · ${en.appName}`;
  }, []);
  const drivers = useQuery({
    queryKey: ["readiness-drivers", blockedOnly],
    queryFn: () => readinessApi.drivers(blockedOnly),
  });
  return (
    <div className="page">
      <div className="page-title">
        <div>
          <span className="eyebrow">Operations / People</span>
          <h1>People</h1>
          <p className="ui-muted">Keep your team and their work context up to date.</p>
        </div>
      </div>
      <label className="checkbox-field">
        <input
          type="checkbox"
          aria-label="Blocked only"
          checked={blockedOnly}
          onChange={(event) => setBlockedOnly(event.target.checked)}
        />
        Blocked only
      </label>
      {drivers.data?.items.length ? (
        <Panel title="Drivers">
          {drivers.data.items.map((driver) => (
            <div key={driver.id}>
              <strong>{driver.name}</strong>
              {driver.enrolled && <span>{en.enrolment.enrolled}</span>}
              {canSendEnrolment && <SendEnrolment id={driver.id} name={driver.name} />}
              {driver.canDrive ? (
                <>
                  <StatusPill status="usable" />
                  <span>Can drive</span>
                </>
              ) : (
                <>
                  <StatusPill status="blocked" />
                  {driver.causes.map((cause) => {
                    const text = peopleCauseText(cause);
                    return text ? <span key={`${cause.code}-${cause.documentType}`}>{text}</span> : null;
                  })}
                </>
              )}
            </div>
          ))}
        </Panel>
      ) : (
        <EmptyState heading="Nothing here yet">There is no work to show in this area yet.</EmptyState>
      )}
    </div>
  );
}
