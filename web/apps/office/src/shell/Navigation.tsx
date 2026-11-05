import { Bell, CalendarClock, CarFront, FileText, MoreHorizontal, Settings, Users } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Link, NavLink, useLocation } from "react-router";
import { en } from "../i18n/en";

export type NavigationItem = { id: string; label: string; icon: typeof Bell };
export const navigation: NavigationItem[] = [
  { id: "attention", label: en.nav.attention, icon: Bell },
  { id: "timesheets", label: en.nav.timesheets, icon: CalendarClock },
  { id: "fleet", label: en.nav.fleet, icon: CarFront },
  { id: "people", label: en.nav.people, icon: Users },
  { id: "documents", label: en.nav.documents, icon: FileText },
  { id: "payroll", label: en.nav.payroll, icon: FileText },
];
function routeFor(id: string) {
  return id === "attention" ? "/" : `/${id}`;
}
export function NavigationLink({ item, compact = false }: { item: NavigationItem; compact?: boolean }) {
  const Icon = item.icon;
  return (
    <NavLink
      to={routeFor(item.id)}
      end={item.id === "attention"}
      aria-label={compact ? item.label : undefined}
    >
      <Icon size={18} aria-hidden />
      <span className={compact ? "visually-hidden" : undefined}>{item.label}</span>
    </NavLink>
  );
}
export function Sidebar() {
  return (
    <aside className="sidebar">
      <Link to="/" className="brand">
        <span className="brand-mark">F</span>
        <span>FleetOps</span>
      </Link>
      <nav aria-label="Primary">
        {navigation.map((item) => (
          <NavigationLink key={item.id} item={item} />
        ))}
        <NavLink end className="ui-button ghost settings-footer" to="/settings">
          <Settings size={18} aria-hidden />
          <span>{en.nav.settings}</span>
        </NavLink>
      </nav>
    </aside>
  );
}
export function TabletRail() {
  return (
    <aside className="icon-rail">
      <nav aria-label="Primary">
        <Link to="/" className="brand-mark" aria-label="FleetOps">
          F
        </Link>
        {navigation.map((item) => (
          <NavigationLink key={item.id} item={item} compact />
        ))}
        <NavLink end className="rail-settings" to="/settings">
          <Settings size={18} aria-hidden />
          <span className="visually-hidden">{en.nav.settings}</span>
        </NavLink>
      </nav>
    </aside>
  );
}
export function MobileNavigation() {
  const [moreOpen, setMoreOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const menu = useRef<HTMLDivElement>(null);
  const { pathname } = useLocation();
  const moreCurrent = ["/documents", "/payroll", "/settings"].includes(
    pathname.toLowerCase().replace(/\/$/, ""),
  );
  useEffect(() => {
    setMoreOpen(false);
  }, [pathname]);
  useEffect(() => {
    if (!moreOpen) return;
    const close = (event: MouseEvent) => {
      if (!menu.current?.contains(event.target as Node) && !trigger.current?.contains(event.target as Node))
        setMoreOpen(false);
    };
    const key = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setMoreOpen(false);
        trigger.current?.focus();
      }
    };
    document.addEventListener("mousedown", close);
    document.addEventListener("keydown", key);
    return () => {
      document.removeEventListener("mousedown", close);
      document.removeEventListener("keydown", key);
    };
  }, [moreOpen]);
  return (
    <nav className="bottom-nav" aria-label="Mobile navigation">
      {navigation.slice(0, 4).map((item) => (
        <NavigationLink key={item.id} item={item} />
      ))}
      <button
        ref={trigger}
        type="button"
        className={moreOpen ? "more-trigger active" : "more-trigger"}
        aria-current={moreCurrent ? "page" : undefined}
        aria-expanded={moreOpen}
        aria-controls="mobile-more-links"
        onClick={() => setMoreOpen((open) => !open)}
      >
        <MoreHorizontal size={18} aria-hidden />
        <span>{en.nav.more}</span>
      </button>
      {moreOpen && (
        <div ref={menu} id="mobile-more-links" className="more-menu">
          <Link to="/documents" onClick={() => setMoreOpen(false)}>
            {en.nav.documents}
          </Link>
          <Link to="/payroll" onClick={() => setMoreOpen(false)}>
            {en.nav.payroll}
          </Link>
          <Link to="/settings" onClick={() => setMoreOpen(false)}>
            {en.nav.settings}
          </Link>
        </div>
      )}
    </nav>
  );
}
