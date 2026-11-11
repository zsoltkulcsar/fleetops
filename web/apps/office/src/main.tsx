import React from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter, Navigate, Route, Routes, useParams } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DevGalleryRoute, OfficePage, Shell } from "./shell/Shell";
import "./shell/shell.css";
import { AuthGate } from "./auth/AuthGate";
import { installAuthExpiryHandler } from "./auth/api";
import { Defects } from "./fleet/Defects";
import { Maintenance } from "./fleet/Maintenance";
import { Fleet } from "./fleet/Fleet";
import { FleetRecord } from "./fleet/FleetRecord";
import { Documents } from "./documents/Documents";
import { People } from "./people/People";
import { Settings } from "./settings/Settings";
import { Attention } from "./attention/Attention";
import { Schedule } from "./schedule/Schedule";
import { Timesheets } from "./timesheets/Timesheets";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (failureCount, error) => {
        const status = (error as { status?: unknown }).status;
        if (typeof status === "number" && status >= 400 && status < 500) return false;
        return failureCount < 1;
      },
      retryDelay: 100,
    },
  },
});
installAuthExpiryHandler();
const page = (section: string) => <OfficePage section={section} />;
function CanonicalRedirect() {
  const path = window.location.pathname.toLowerCase().replace(/\/$/, "") || "/";
  const known = ["/", "/schedule", "/timesheets", "/fleet", "/people", "/documents", "/payroll", "/settings"];
  if (import.meta.env.DEV) known.push("/__gallery");
  return <Navigate to={known.includes(path) ? path : "/"} replace />;
}
createRoot(document.getElementById("app")!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route
            element={
              <AuthGate>
                <Shell />
              </AuthGate>
            }
          >
            <Route path="/" element={<Attention />} />
            <Route path="/timesheets" element={<Timesheets />} />
            <Route path="/schedule" element={<Schedule />} />
            <Route path="/fleet" element={<Fleet />} />
            <Route path="/fleet/defects" element={<Defects />} />
            <Route path="/fleet/maintenance" element={<Maintenance />} />
            <Route path="/fleet/:id" element={<FleetRecordRoute />} />
            <Route path="/people" element={<People />} />
            <Route path="/documents" element={<Documents />} />
            <Route path="/payroll" element={page("payroll")} />
            <Route path="/settings" element={<Settings />} />
            {import.meta.env.DEV && <Route path="/__gallery" element={<DevGalleryRoute />} />}
          </Route>
          <Route path="*" element={<CanonicalRedirect />} />
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  </React.StrictMode>,
);

function FleetRecordRoute() {
  const { id = "" } = useParams();
  return <FleetRecord id={id} />;
}
