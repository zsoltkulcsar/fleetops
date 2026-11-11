import { Component, type ErrorInfo, type ReactNode, useEffect, useRef, useState } from "react";
import { Outlet, useLocation } from "react-router";
import { Banner, EmptyState } from "@fleetops/ui";
import { apiClient } from "@fleetops/api-client";
import { en } from "../i18n/en";
import { Gallery } from "./Gallery";
import { Header } from "./Header";
import { MobileNavigation, Sidebar, TabletRail } from "./Navigation";
import { formatTodayBerlin } from "./date";
export { formatTodayBerlin } from "./date";

type LayoutMode = "wide" | "tablet" | "mobile";
const MEDIA_QUERIES = { tablet: "(width >= 768px)", wide: "(width >= 1280px)" } as const;
function getLayoutMode(): LayoutMode {
  if (typeof window === "undefined" || window.matchMedia(MEDIA_QUERIES.wide).matches) return "wide";
  return window.matchMedia(MEDIA_QUERIES.tablet).matches ? "tablet" : "mobile";
}
export function useOnline() {
  const [online, setOnline] = useState(() => (typeof navigator === "undefined" ? true : navigator.onLine));
  useEffect(() => {
    const on = () => setOnline(true);
    const off = () => setOnline(false);
    window.addEventListener("online", on);
    window.addEventListener("offline", off);
    return () => {
      window.removeEventListener("online", on);
      window.removeEventListener("offline", off);
    };
  }, []);
  return online;
}
function normalisePath(pathname: string) {
  const path = pathname.toLowerCase().replace(/\/$/, "");
  return path || "/";
}
const routeNames: Record<string, string> = {
  "/": en.nav.attention,
  "/timesheets": en.nav.timesheets,
  "/schedule": en.fleetTabs.schedule,
  "/fleet": en.nav.fleet,
  "/fleet/defects": "Defects",
  "/fleet/maintenance": "Maintenance",
  "/people": en.nav.people,
  "/documents": en.nav.documents,
  "/payroll": en.nav.payroll,
  "/settings": en.nav.settings,
};
export function Shell() {
  const location = useLocation();
  const [layoutMode, setLayoutMode] = useState<LayoutMode>(getLayoutMode);
  const initialPath = useRef(location.pathname);
  void apiClient;
  useEffect(() => {
    const update = () => setLayoutMode(getLayoutMode());
    const tablet = window.matchMedia(MEDIA_QUERIES.tablet);
    const wide = window.matchMedia(MEDIA_QUERIES.wide);
    tablet.addEventListener("change", update);
    wide.addEventListener("change", update);
    return () => {
      tablet.removeEventListener("change", update);
      wide.removeEventListener("change", update);
    };
  }, []);
  useEffect(() => {
    if (location.pathname === initialPath.current) return;
    const frame = requestAnimationFrame(() =>
      document.querySelector<HTMLElement>("#main-content h1")?.focus(),
    );
    return () => cancelAnimationFrame(frame);
  }, [location.pathname]);
  return (
    <div className="office-shell bg-bg text-ink">
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>
      {layoutMode === "wide" && <Sidebar />}
      {layoutMode === "tablet" && <TabletRail />}
      <Header pageName={routeNames[normalisePath(location.pathname)] ?? en.nav.attention} />
      <main id="main-content" className="content" tabIndex={-1}>
        <RouteErrorBoundary key={location.pathname}>
          <Outlet />
        </RouteErrorBoundary>
      </main>
      {layoutMode === "mobile" && <MobileNavigation />}
    </div>
  );
}
export function OfficePage({ section }: { section: string }) {
  const location = useLocation();
  const heading = useRef<HTMLHeadingElement>(null);
  const online = useOnline();
  const label = routeNames[normalisePath(location.pathname)] ?? section;
  useEffect(() => {
    document.title = `${label} · ${en.appName}`;
  }, [label]);
  return (
    <div className="page">
      <div className="page-title">
        <div>
          <span className="eyebrow" data-testid="today">
            Today · {formatTodayBerlin()}
          </span>
          <h1 ref={heading} tabIndex={-1}>
            {label}
          </h1>
          <p className="ui-muted">
            {en.subtitles[section as keyof typeof en.subtitles] ?? en.subtitles.attention}
          </p>
        </div>
      </div>
      {!online && (
        <Banner kind="offline" label={en.banner.offline}>
          {en.states.offline}
        </Banner>
      )}
      <EmptyState heading={en.states.empty}>{en.states.emptyBody}</EmptyState>
    </div>
  );
}
export function DevGalleryRoute() {
  useEffect(() => {
    document.title = `Gallery · ${en.appName}`;
  }, []);
  return import.meta.env.DEV ? <Gallery /> : null;
}
export class RouteErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  state = { error: null as Error | null };
  static getDerivedStateFromError(error: Error) {
    return { error };
  }
  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(error, info);
  }
  render() {
    return this.state.error ? (
      <div className="page">
        <Banner
          kind="error"
          label={en.banner.error}
          retryLabel={en.actions.tryAgain}
          onRetry={() => this.setState({ error: null })}
        >
          {en.states.error}
        </Banner>
      </div>
    ) : (
      this.props.children
    );
  }
}
