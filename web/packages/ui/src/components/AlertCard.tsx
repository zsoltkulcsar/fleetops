import type { ReactNode } from "react";
import { StatusPill, type AlertSeverity } from "./StatusPill";

export type AlertItem = { subject: string; body: ReactNode; severity?: AlertSeverity };
export function AlertCard({
  items,
  actionLabel,
  onAction,
}: {
  items: AlertItem[];
  actionLabel: string;
  onAction: () => void;
}) {
  return (
    <div>
      {items.map((item, index) => (
        <article className="ui-panel alert-card" key={`${item.subject}-${index}`}>
          <StatusPill status={item.severity ?? "warning"} />
          <div>
            <h3>{item.subject}</h3>
            <p className="ui-muted">{item.body}</p>
          </div>
          <button type="button" className="ui-button" onClick={onAction}>
            {actionLabel}
          </button>
        </article>
      ))}
    </div>
  );
}
