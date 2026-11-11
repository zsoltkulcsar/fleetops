import { StatusPill } from "@fleetops/ui";
import type { PersonRow } from "./api";
import { personStatus, reasonLabels } from "./labels";

export function PersonStatus({ status }: { status: string }) {
  return (
    <StatusPill
      status={status === "active" ? "usable" : status === "suspended" ? "blocked" : "missing"}
      label={personStatus(status)}
    />
  );
}
export function CanDrive({ person }: { person: PersonRow }) {
  return (
    <span>
      <StatusPill status={person.canDrive ? "usable" : "blocked"} label={person.canDrive ? "Yes" : "No"} />
      {!person.canDrive && person.reasonCategory && (
        <span className="people-reason">{reasonLabels[person.reasonCategory] ?? "Document restriction"}</span>
      )}
    </span>
  );
}
