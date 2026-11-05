import type { ReactNode } from "react";
import { StatusPill, statusVocabulary, type StatusName } from "./StatusPill";

export function WeekGridCell({
  status = "valid",
  date,
  children,
}: {
  status?: StatusName;
  date: string;
  children?: ReactNode;
}) {
  const pill =
    status === "expiring" || status === "expired" ? (
      <StatusPill status={status} date={date} />
    ) : status === "absence" ? (
      <StatusPill status={status} label={statusVocabulary[status].label} />
    ) : (
      <StatusPill status={status} label={statusVocabulary[status].label} />
    );
  return (
    <div className="week-cell">
      <span className="ui-muted">{date}</span>
      {pill}
      {children}
    </div>
  );
}
