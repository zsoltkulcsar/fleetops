import { Hourglass } from "lucide-react";
import type { ReactNode } from "react";

/** Greyed placeholder for data FleetOps does not store yet (Epic 15 backlog, decisions Round 7). */
export function ComingLater({
  title,
  children,
  compact = false,
}: {
  title: string;
  children?: ReactNode;
  compact?: boolean;
}) {
  if (compact)
    return (
      <span className="coming-later-chip" title={`${title}: coming in Epic 15`}>
        <Hourglass size={12} aria-hidden />
        {title}
        <span className="visually-hidden">: coming in Epic 15</span>
      </span>
    );
  return (
    <div className="coming-later">
      <div className="coming-later-head">
        <span className="eyebrow">{title}</span>
        <span className="coming-later-tag">
          <Hourglass size={12} aria-hidden /> Coming in Epic 15
        </span>
      </div>
      {children && <div className="coming-later-body">{children}</div>}
    </div>
  );
}
