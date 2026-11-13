import { CalendarRange, Clock, Users } from "lucide-react";
import { Link } from "react-router";

/** Segmented switch shared by People and Absences (Round 7 reference layout). Timesheets keeps its own nav item. */
export function PeopleViews({
  current,
  people,
  awaitingShifts,
}: {
  current: "people" | "absences";
  people?: number | null;
  awaitingShifts?: number | null;
}) {
  return (
    <nav className="people-views" aria-label="People views">
      <Link to="/people" aria-current={current === "people" ? "page" : undefined}>
        <Users size={16} aria-hidden />
        People
        {people != null && <span className="people-views-count"> {people}</span>}
      </Link>
      <Link to="/timesheets">
        <Clock size={16} aria-hidden />
        Timesheets
        {awaitingShifts ? <span className="people-views-count warn"> {awaitingShifts} awaiting</span> : null}
      </Link>
      <Link to="/people/absences" aria-current={current === "absences" ? "page" : undefined}>
        <CalendarRange size={16} aria-hidden />
        Absences & capacity
      </Link>
    </nav>
  );
}
