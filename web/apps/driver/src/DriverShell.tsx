import { CalendarDays, CircleUserRound, TriangleAlert, Truck } from "lucide-react";
import { NavLink, Navigate, Route, Routes } from "react-router";
import { en } from "./i18n/en";
import { useState } from "react";
import { useShifts } from "./useShifts";
import { DriverDay } from "./DriverDay";
import { DriverWeek } from "./DriverWeek";
import { useSnapshot } from "./useSnapshot";
import { DriverSchedule } from "./DriverSchedule";
import { DriverDocuments } from "./DriverDocuments";
import { DriverTruck } from "./DriverTruck";
import { QueueStatus } from "./QueueStatus";
import { DriverDefect } from "./DriverDefect";
import { DriverNotifications } from "./DriverNotifications";

export function DriverShell({
  name,
  officePhone,
  busy,
  onSignOut,
}: {
  name: string;
  officePhone: string;
  busy: boolean;
  onSignOut: () => Promise<void>;
}) {
  const state = useSnapshot();
  const shifts = useShifts();
  const [truck, setTruck] = useState<string>();
  const pages = [
    { path: "shift", title: en.shift, icon: Truck },
    { path: "week", title: en.week, icon: CalendarDays },
    { path: "defect", title: en.defect, icon: TriangleAlert },
    { path: "me", title: en.me, icon: CircleUserRound },
  ];
  return (
    <>
      <DriverNotifications online={state.online} />
      <QueueStatus online={state.online} />
      <Routes>
        <Route
          path="shift"
          element={<DriverDay state={shifts} driver={state.snapshot} name={name} officePhone={officePhone} />}
        />
        <Route
          path="week"
          element={
            <>
              <DriverWeek state={shifts} />
              <DriverSchedule {...state} embedded onRefresh={state.refresh} onTruck={setTruck} />
            </>
          }
        />
        <Route
          path="me"
          element={
            <DriverDocuments
              snapshot={state.snapshot}
              online={state.online}
              onRefresh={state.refresh}
              onSignOut={onSignOut}
              busy={busy}
            />
          }
        />
        <Route
          path="defect"
          element={<DriverDefect snapshot={state.snapshot} shifts={shifts} officePhone={officePhone} />}
        />
        <Route path="*" element={<Navigate to="/shift" replace />} />
      </Routes>
      {truck && <DriverTruck id={truck} online={state.online} onClose={() => setTruck(undefined)} />}
      <nav className="driver-nav" aria-label={en.navigation}>
        {pages.map(({ path, title, icon: Icon }) => (
          <NavLink key={path} to={`/${path}`}>
            <Icon size={24} aria-hidden="true" />
            <span>{title}</span>
          </NavLink>
        ))}
      </nav>
    </>
  );
}
