import { peopleApi } from "../people/api";
import { personRoles, personStatus } from "../people/labels";
import { en } from "../i18n/en";
import { useQueryClient } from "@tanstack/react-query";
import { authApi } from "../auth/api";
import { documentsApi } from "../documents/api";
import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router";
import { Bell, LogOut, Menu, Search, Truck } from "lucide-react";
import { OPEN_MOBILE_MENU } from "./Navigation";
import { apiClient } from "@fleetops/api-client";
import { useQuery } from "@tanstack/react-query";
import { formatBerlinDateTime } from "./date";

export function Header({ pageName }: { pageName: string }) {
  const client = useQueryClient();
  const navigate = useNavigate();
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [results, setResults] = useState<Awaited<ReturnType<typeof documentsApi.searchAll>> | null>(null);
  const people = useQuery({
    queryKey: ["people-search", term],
    queryFn: () => peopleApi.list({ search: term, pageSize: 10 }),
    enabled: term.length >= 2,
  });
  const [notificationsOpen, setNotificationsOpen] = useState(false);
  const notifications = useQuery({
    queryKey: ["notifications"],
    queryFn: async () => {
      const response = await apiClient.GET("/api/v1/notifications", { params: { query: { limit: 20 } } });
      return response.data;
    },
    refetchInterval: 60_000,
  });
  async function signOut() {
    await authApi.signOut();
    client.setQueryData(["session"], null);
  }
  useEffect(() => {
    const timer = window.setTimeout(() => setTerm(search.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [search]);
  useEffect(() => {
    if (term.length < 2) {
      setResults(null);
      return;
    }
    let active = true;
    setResults(null);
    void documentsApi
      .searchAll(term)
      .then((result) => {
        if (active) setResults(result);
      })
      .catch(() => {
        if (active) setResults(null);
      });
    return () => {
      active = false;
    };
  }, [term]);
  useEffect(() => {
    if (!notificationsOpen) return;
    const close = (event: KeyboardEvent) => {
      if (event.key === "Escape") setNotificationsOpen(false);
    };
    document.addEventListener("keydown", close);
    return () => document.removeEventListener("keydown", close);
  }, [notificationsOpen]);
  async function markRead(id: string, link: string) {
    await apiClient.POST("/api/v1/notifications/{id}/read", { params: { path: { id } } });
    await client.invalidateQueries({ queryKey: ["notifications"] });
    setNotificationsOpen(false);
    navigate(link);
  }
  async function markAllRead() {
    await apiClient.POST("/api/v1/notifications/read-all");
    await client.invalidateQueries({ queryKey: ["notifications"] });
  }
  useEffect(() => {
    const key = (event: KeyboardEvent) => {
      const target = event.target;
      const isTextEntry =
        target instanceof HTMLInputElement ||
        target instanceof HTMLTextAreaElement ||
        target instanceof HTMLSelectElement ||
        (target instanceof HTMLElement && target.isContentEditable);
      if (
        (event.key === "/" || event.code === "Slash") &&
        !isTextEntry &&
        !event.defaultPrevented &&
        !document.querySelector(".timesheets-page")
      ) {
        event.preventDefault();
        document.getElementById("office-search")?.focus();
      }
    };
    const focusSearch = () => document.getElementById("office-search")?.focus();
    document.addEventListener("keydown", key);
    window.addEventListener("fleetops:focus-search", focusSearch);
    return () => {
      document.removeEventListener("keydown", key);
      window.removeEventListener("fleetops:focus-search", focusSearch);
    };
  }, []);
  return (
    <header className="topbar">
      <button
        type="button"
        className="topbar-menu"
        aria-label="Open menu"
        onClick={(event) =>
          window.dispatchEvent(new CustomEvent(OPEN_MOBILE_MENU, { detail: event.currentTarget }))
        }
      >
        <Menu size={20} aria-hidden />
      </button>
      <span className="topbar-brand" aria-hidden>
        <Truck size={18} />
      </span>
      <div className="topbar-title">
        <span className="eyebrow">{en.header.workspace}</span>
        <strong>{pageName}</strong>
      </div>
      <div className="topbar-actions">
        <div className="office-search">
          <Search className="office-search-icon" size={16} aria-hidden />
          <label className="visually-hidden" htmlFor="office-search">
            {en.header.searchLabel}
          </label>
          <input
            id="office-search"
            aria-label={en.header.searchLabel}
            placeholder={en.header.searchPlaceholder}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
          {term.length >= 2 && (
            <div className="search-popover">
              {!results?.documents.length && !results?.vehicles.length && !people.data?.items.length ? (
                <span>{en.header.noResults}</span>
              ) : (
                <>
                  {Boolean(results?.documents.length) && (
                    <section>
                      <strong>{en.header.documents}</strong>
                      {results?.documents.map((item) => (
                        <button
                          key={item.id}
                          type="button"
                          onClick={() => {
                            navigate(`/documents?open=${item.id}`);
                            setSearch("");
                          }}
                        >
                          {en.documents.types[item.type]} {item.number ?? ""} {"\u00b7"} {item.ownerName}
                        </button>
                      ))}
                    </section>
                  )}
                  {Boolean(results?.vehicles.length) && (
                    <section>
                      <strong>{en.header.vehicles}</strong>
                      {results?.vehicles.map((item) => (
                        <Link key={item.id} to={`/fleet/${item.id}`}>
                          {item.plate} {"\u00b7"} {item.make} {item.model}
                        </Link>
                      ))}
                    </section>
                  )}
                  {Boolean(people.data?.items.length) && (
                    <section>
                      <strong>{en.header.people}</strong>
                      {people.data?.items.map((item) => (
                        <Link key={item.id} to={`/people/${item.id}`} onClick={() => setSearch("")}>
                          {item.name} {"\u00b7"} {personRoles(item.roles)} {"\u00b7"}{" "}
                          {personStatus(item.status)}
                        </Link>
                      ))}
                    </section>
                  )}
                </>
              )}
            </div>
          )}
        </div>
        <div className="notification-menu">
          <button
            className="ui-button ghost notification-button"
            type="button"
            aria-label={en.notifications.unreadLabel(notifications.data?.unread ?? 0)}
            aria-expanded={notificationsOpen}
            onClick={() => setNotificationsOpen((open) => !open)}
          >
            <Bell size={18} aria-hidden="true" />
            <span>{notifications.data?.unread ?? 0}</span>
          </button>
          {notificationsOpen && (
            <div className="notification-popover" role="dialog" aria-label={en.notifications.title}>
              <div className="notification-heading">
                <strong>{en.notifications.title}</strong>
                <button className="ui-button ghost" type="button" onClick={() => void markAllRead()}>
                  {en.notifications.markAll}
                </button>
              </div>
              {notifications.data?.items.length ? (
                <div className="notification-list">
                  {notifications.data.items.map((item) => (
                    <button
                      className={`notification-row${item.readAt ? "" : " unread"}`}
                      key={item.id}
                      type="button"
                      onClick={() => void markRead(item.id, item.link)}
                    >
                      <span className="notification-row-text">{item.text}</span>
                      <span className="notification-row-meta">
                        {!item.readAt && (
                          <>
                            <span className="notification-dot" aria-hidden="true" />
                            {en.notifications.newLabel}
                          </>
                        )}
                        {formatBerlinDateTime(item.createdAt)}
                      </span>
                    </button>
                  ))}
                </div>
              ) : (
                <span className="notification-empty">{en.notifications.empty}</span>
              )}
            </div>
          )}
        </div>
        <Link className="ui-button ghost topbar-my-notifications" to="/me/notifications">
          My notifications
        </Link>
        <button className="ui-button ghost topbar-signout" type="button" onClick={signOut}>
          <LogOut className="topbar-signout-icon" size={18} aria-hidden />
          <span className="topbar-signout-label">{en.auth.signOut}</span>
        </button>
      </div>
    </header>
  );
}
