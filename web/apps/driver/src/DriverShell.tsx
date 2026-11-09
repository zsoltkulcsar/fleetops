import { CalendarDays, CircleUserRound, TriangleAlert, Truck } from "lucide-react";
import { NavLink, Navigate, Route, Routes } from "react-router";
import { en } from "./i18n/en";

export function DriverShell({
  name,
  busy,
  onSignOut,
}: {
  name: string;
  busy: boolean;
  onSignOut: () => Promise<void>;
}) {
  const pages = [
    { path: "shift", title: en.shift, icon: Truck },
    { path: "week", title: en.week, icon: CalendarDays },
    { path: "defect", title: en.defect, icon: TriangleAlert },
    { path: "me", title: en.me, icon: CircleUserRound },
  ];
  return (
    <>
      <Routes>
        {pages.map((page) => (
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
