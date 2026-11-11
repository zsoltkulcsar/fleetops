import { useDefectAccess } from "./defectPresentation";
import { NavLink } from "react-router";
import "./fleet.css";
import { en } from "../i18n/en";

// Schedule is a tab of Fleet (design/ia.md: Fleet contains O-18, Schedule tab).
export function FleetTabs() {
  const { canRead } = useDefectAccess();
  return (
    <div className="fleet-tabs" role="group" aria-label={en.fleetTabs.label}>
      <NavLink to="/fleet" end>
        {en.fleetTabs.vehicles}
      </NavLink>
      <NavLink to="/schedule">{en.fleetTabs.schedule}</NavLink>
      {canRead && (
        <>
          <NavLink to="/fleet/defects">Defects</NavLink>
          <NavLink to="/fleet/maintenance">Maintenance</NavLink>
        </>
      )}
    </div>
  );
}
