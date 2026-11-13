import { useDefectAccess } from "./defectPresentation";
import { NavLink } from "react-router";
import { TriangleAlert, Truck, Wrench } from "lucide-react";
import "./fleet.css";
import { en } from "../i18n/en";

/** Segmented switch between the Fleet views (Round 7 reference layout); counts are optional. */
export function FleetTabs({
  vehicles,
  openDefects,
  overdue,
}: {
  vehicles?: number | null;
  openDefects?: number | null;
  overdue?: number | null;
} = {}) {
  const { canRead } = useDefectAccess();
  return (
    <div className="view-switch fleet-views" role="group" aria-label={en.fleetTabs.label}>
      <NavLink to="/fleet" end>
        <Truck size={16} aria-hidden />
        {en.fleetTabs.vehicles}
        {vehicles != null && <span className="view-switch-count"> {vehicles}</span>}
      </NavLink>
      {canRead && (
        <>
          <NavLink to="/fleet/defects">
            <TriangleAlert size={16} aria-hidden />
            Defects
            {openDefects ? <span className="view-switch-count warn"> {openDefects} open</span> : null}
          </NavLink>
          <NavLink to="/fleet/maintenance">
            <Wrench size={16} aria-hidden />
            Maintenance
            {overdue ? <span className="view-switch-count crit"> {overdue} overdue</span> : null}
          </NavLink>
        </>
      )}
    </div>
  );
}
