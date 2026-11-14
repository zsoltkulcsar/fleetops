import {
  Bell,
  CalendarClock,
  CalendarRange,
  FileText,
  Menu,
  Settings,
  SlidersHorizontal,
  Truck,
  Users,
  Wallet,
  X,
} from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Link, NavLink, useLocation } from "react-router";
import { en } from "../i18n/en";
import { useSession } from "../auth/AuthGate";
import { useQuery } from "@tanstack/react-query";
import { documentsApi } from "../documents/api";

export type NavigationItem = { id: string; label: string; icon: typeof Truck };
export const navigation: NavigationItem[] = [
  { id: "attention", label: en.nav.attention, icon: SlidersHorizontal },
  { id: "plan", label: en.nav.plan, icon: CalendarRange },
  { id: "timesheets", label: en.nav.timesheets, icon: CalendarClock },
  { id: "fleet", label: en.nav.fleet, icon: Truck },
  { id: "people", label: en.nav.people, icon: Users },
  { id: "documents", label: en.nav.documents, icon: FileText },
  { id: "payroll", label: en.nav.payroll, icon: CalendarClock },
];
function visibleItems(roles: string[]) {
  const owner = roles.includes("owner");
  const payrollAllowed = owner || roles.includes("hr");
  return navigation.filter((item) => item.id !== "payroll" || payrollAllowed);
}
function routeFor(id: string) {
  return id === "attention" ? "/" : `/${id}`;
}
export function NavigationLink({ item, compact = false }: { item: NavigationItem; compact?: boolean }) {
  const Icon = item.icon;
  const counts = useQuery({
    queryKey: ["document-counts"],
    queryFn: documentsApi.counts,
    enabled: item.id === "documents",
    refetchInterval: 60_000,
  });
  const queue = counts.data?.queue ?? 0;
  return (
    <NavLink
      to={routeFor(item.id)}
      end={item.id === "attention"}
      aria-label={compact ? item.label : undefined}
    >
      <Icon size={18} aria-hidden />
      <span className={compact ? "visually-hidden" : undefined}>{item.label}</span>
      {item.id === "documents" && queue > 0 && (
        <span className="nav-badge" aria-label={`${queue} to verify`}>
          {queue}
        </span>
      )}
    </NavLink>
  );
}
export function Sidebar() {
  const { data } = useSession();
  const items = visibleItems(data?.roles ?? []);
  const canSettings = data?.roles.includes("owner") || data?.roles.includes("hr");
  return (
    <aside className="sidebar">
      <Link to="/" className="brand">
        <span className="brand-mark">
          <Truck size={20} aria-hidden />
        </span>
        <span className="brand-text">
          <span>FleetOps</span>
          <small>Office</small>
        </span>
      </Link>
      <nav aria-label="Primary">
        {items.map((item) => (
          <NavigationLink key={item.id} item={item} />
        ))}
        {canSettings && (
          <NavLink end className="ui-button ghost settings-footer" to="/settings">
            <Settings size={18} aria-hidden />
            <span>{en.nav.settings}</span>
          </NavLink>
        )}
      </nav>
    </aside>
  );
}
export function TabletRail() {
  const { data } = useSession();
  const items = visibleItems(data?.roles ?? []);
  const canSettings = data?.roles.includes("owner") || data?.roles.includes("hr");
  return (
    <aside className="icon-rail">
      <nav aria-label="Primary">
        <Link to="/" className="brand-mark" aria-label="FleetOps">
          <Truck size={20} aria-hidden />
        </Link>
        {items.map((item) => (
          <NavigationLink key={item.id} item={item} compact />
        ))}
        {canSettings && (
          <NavLink end className="rail-settings" to="/settings">
            <Settings size={18} aria-hidden />
            <span className="visually-hidden">{en.nav.settings}</span>
          </NavLink>
        )}
      </nav>
    </aside>
  );
}
/** Opens the phone menu from anywhere in the shell; the header menu button sends it with itself as detail. */
export const OPEN_MOBILE_MENU = "fleetops:open-mobile-menu";
const moreRoutes = ["/plan", "/documents", "/payroll", "/settings", "/me/notifications"];
/** Phone navigation after the 2026-11-14 mobile reference: four tabs and More, which opens a dark menu panel
    sliding in from the left with the remaining pages. */
export function MobileNavigation() {
  const { data } = useSession();
  const items = visibleItems(data?.roles ?? []);
  const canSettings = data?.roles.includes("owner") || data?.roles.includes("hr");
  const [moreOpen, setMoreOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const opener = useRef<HTMLElement | null>(null);
  const menu = useRef<HTMLDivElement>(null);
  const { pathname } = useLocation();
  const moreCurrent = moreRoutes.includes(pathname.toLowerCase().replace(/\/$/, ""));
  useEffect(() => {
    setMoreOpen(false);
  }, [pathname]);
  useEffect(() => {
    const open = (event: Event) => {
      const detail = event instanceof CustomEvent ? event.detail : null;
      opener.current = detail instanceof HTMLElement ? detail : null;
      setMoreOpen(true);
    };
    window.addEventListener(OPEN_MOBILE_MENU, open);
    return () => window.removeEventListener(OPEN_MOBILE_MENU, open);
  }, []);
  useEffect(() => {
    if (!moreOpen) return;
    const close = (event: MouseEvent) => {
      const target = event.target as Node;
      if (
        !menu.current?.contains(target) &&
        !trigger.current?.contains(target) &&
        !opener.current?.contains(target)
      )
        setMoreOpen(false);
    };
    const key = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setMoreOpen(false);
        (opener.current ?? trigger.current)?.focus();
      }
    };
    document.addEventListener("mousedown", close);
    document.addEventListener("keydown", key);
    return () => {
      document.removeEventListener("mousedown", close);
      document.removeEventListener("keydown", key);
    };
  }, [moreOpen]);
  const closeMenu = () => setMoreOpen(false);
  const menuLink = (to: string, label: string, Icon: typeof Truck) => (
    <NavLink to={to} end onClick={closeMenu}>
      <Icon size={18} aria-hidden />
      <span>{label}</span>
    </NavLink>
  );
  return (
    <>
      <nav className="bottom-nav" aria-label="Mobile navigation">
        {items
          .filter((item) => item.id !== "plan")
          .slice(0, 4)
          .map((item) => (
            <NavigationLink key={item.id} item={item} />
          ))}
        <button
          ref={trigger}
          type="button"
          className={moreOpen ? "more-trigger active" : "more-trigger"}
          aria-current={moreCurrent ? "page" : undefined}
          aria-expanded={moreOpen}
          aria-controls="mobile-more-links"
          onClick={() => {
            opener.current = null;
            setMoreOpen((open) => !open);
          }}
        >
          <Menu size={18} aria-hidden />
          <span>{en.nav.more}</span>
        </button>
      </nav>
      {moreOpen && (
        <>
          <div className="mobile-menu-backdrop" aria-hidden />
          <div ref={menu} id="mobile-more-links" className="mobile-menu">
            <div className="mobile-menu-brand">
              <span className="brand-mark">
                <Truck size={20} aria-hidden />
              </span>
              <span className="brand-text">
                <span>FleetOps</span>
                <small>Office</small>
              </span>
              <button type="button" className="mobile-menu-close" aria-label="Close menu" onClick={closeMenu}>
                <X size={18} aria-hidden />
              </button>
            </div>
            <nav aria-label="More pages">
              {menuLink("/plan", en.nav.plan, CalendarRange)}
              {menuLink("/documents", en.nav.documents, FileText)}
              {items.some((item) => item.id === "payroll") && menuLink("/payroll", en.nav.payroll, Wallet)}
              {canSettings && menuLink("/settings", en.nav.settings, Settings)}
              {menuLink("/me/notifications", "My notifications", Bell)}
            </nav>
          </div>
        </>
      )}
    </>
  );
}
