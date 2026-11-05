import { Info } from "lucide-react";
import type { ReactNode } from "react";

type Action = { label: string; onClick: () => void };
export function EmptyState({
  heading,
  children,
  primary,
  secondary,
}: {
  heading: string;
  children: ReactNode;
  primary?: Action;
  secondary?: Action;
}) {
  return (
    <div className="empty-state">
      <div className="empty-icon">
        <Info size={22} aria-hidden />
      </div>
      <h2>{heading}</h2>
      <p className="ui-muted">{children}</p>
      {(primary || secondary) && (
        <div>
          {primary && (
            <button type="button" className="ui-button primary" onClick={primary.onClick}>
              {primary.label}
            </button>
          )}
          {secondary && (
            <button type="button" className="ui-button ghost" onClick={secondary.onClick}>
              {secondary.label}
            </button>
          )}
        </div>
      )}
    </div>
  );
}
