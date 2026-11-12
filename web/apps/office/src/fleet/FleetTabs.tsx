import { useDefectAccess } from "./defectPresentation";
import { NavLink } from "react-router";
import "./fleet.css";
import { en } from "../i18n/en";

export function FleetTabs() {
  const { canRead } = useDefectAccess();
  return (
    <div className="fleet-tabs" role="group" aria-label={en.fleetTabs.label}>
      <NavLink to="/fleet" end>
        {en.fleetTabs.vehicles}
      </NavLink>
      {canRead && (
        <>
          <NavLink to="/fleet/defects">Defects</NavLink>
          <NavLink to="/fleet/maintenance">Maintenance</NavLink>
        </>
      )}
    </div>
  );
}
