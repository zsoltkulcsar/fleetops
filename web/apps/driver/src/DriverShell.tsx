import { CalendarDays, CircleUserRound, TriangleAlert, Truck } from "lucide-react";
import { NavLink, Navigate, Route, Routes } from "react-router";
import { en } from "./i18n/en";
import { useState } from "react";
import { useSnapshot } from "./useSnapshot";
import { DriverSchedule } from "./DriverSchedule";
import { DriverDocuments } from "./DriverDocuments";
import { DriverTruck } from "./DriverTruck";
import { DriverNotifications } from "./DriverNotifications";

export function DriverShell({
  name,
  busy,
  onSignOut,
}: {
  name: string;
  busy: boolean;
  onSignOut: () => Promise<void>;
}) {
  const state = useSnapshot();
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
      <Routes>
        <Route
          path="shift"
          element={
            <DriverSchedule {...state} name={name} shift onRefresh={state.refresh} onTruck={setTruck} />
          }
        />
        <Route
          path="week"
          element={<DriverSchedule {...state} onRefresh={state.refresh} onTruck={setTruck} />}
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
        {pages
          .filter((page) => page.path === "defect")
          .map((page) => (
            <Route
              key={page.path}
              path={page.path}
              element={
                <section className="driver-screen">
                  <h1>{page.title}</h1>
                  {page.path === "shift" && (
                    <p className="driver-greeting">{en.greeting(name.trim().split(/\s+/)[0])}</p>
                  )}
                  <p>{en.comingSoon}</p>
                  {page.path === "me" && (
                    <div className="driver-actions">
                      <button
                        disabled={busy}
                        onClick={async () => {
                          if (window.confirm(en.signOutConfirm)) await onSignOut();
                        }}
                      >
                        {busy ? en.working : en.signOut}
                      </button>
                    </div>
                  )}
                </section>
              }
            />
          ))}
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
