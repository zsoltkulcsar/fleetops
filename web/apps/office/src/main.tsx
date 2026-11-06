import React from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DevGalleryRoute, OfficePage, Shell } from "./shell/Shell";
import "./shell/shell.css";
import { AuthGate } from "./auth/AuthGate";
import { installAuthExpiryHandler } from "./auth/api";

const queryClient = new QueryClient();
installAuthExpiryHandler();
const page = (section: string) => <OfficePage section={section} />;
function CanonicalRedirect() {
  const path = window.location.pathname.toLowerCase().replace(/\/$/, "") || "/";
  const known = ["/", "/timesheets", "/fleet", "/people", "/documents", "/payroll", "/settings"];
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
            <Route path="/" element={page("attention")} />
            <Route path="/timesheets" element={page("timesheets")} />
            <Route path="/fleet" element={page("fleet")} />
            <Route path="/people" element={page("people")} />
            <Route path="/documents" element={page("documents")} />
            <Route path="/payroll" element={page("payroll")} />
            <Route path="/settings" element={page("settings")} />
            {import.meta.env.DEV && <Route path="/__gallery" element={<DevGalleryRoute />} />}
          </Route>
          <Route path="*" element={<CanonicalRedirect />} />
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  </React.StrictMode>,
);
