import { en } from "../i18n/en";
import { useQueryClient } from "@tanstack/react-query";
import { authApi } from "../auth/api";
import { documentsApi } from "../documents/api";
import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router";

export function Header({ pageName }: { pageName: string }) {
  const client = useQueryClient();
  const navigate = useNavigate();
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [results, setResults] = useState<Awaited<ReturnType<typeof documentsApi.searchAll>> | null>(null);
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
    void documentsApi
      .searchAll(term)
      .then(setResults)
      .catch(() => setResults(null));
  }, [term]);
  useEffect(() => {
    const key = (event: KeyboardEvent) => {
      const target = event.target;
      const isTextEntry =
        target instanceof HTMLInputElement ||
        target instanceof HTMLTextAreaElement ||
        target instanceof HTMLSelectElement ||
        (target instanceof HTMLElement && target.isContentEditable);
      if ((event.key === "/" || event.code === "Slash") && !isTextEntry) {
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
      <div className="topbar-title">
        <span className="eyebrow">{en.header.workspace}</span>
        <strong>{pageName}</strong>
      </div>
      <div className="topbar-actions">
        <div className="office-search">
          <input
            id="office-search"
            aria-label={en.header.searchLabel}
            placeholder={en.header.searchPlaceholder}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
          {term.length >= 2 && (
            <div className="search-popover">
              {!results ||
              (!results.documents.length && !results.vehicles.length && !results.people.length) ? (
                <span>{en.header.noResults}</span>
              ) : (
                <>
                  {results.documents.length > 0 && (
                    <section>
                      <strong>{en.header.documents}</strong>
                      {results.documents.map((item) => (
                        <button
                          key={item.id}
                          type="button"
                          onClick={() => {
                            navigate(`/documents?open=${item.id}`);
                            setSearch("");
                          }}
                        >
                          {en.documents.types[item.type]} {item.number ?? ""} · {item.ownerName}
                        </button>
                      ))}
                    </section>
                  )}
                  {results.vehicles.length > 0 && (
                    <section>
                      <strong>{en.header.vehicles}</strong>
                      {results.vehicles.map((item) => (
                        <Link key={item.id} to={`/fleet/${item.id}`}>
                          {item.plate} · {item.make} {item.model}
                        </Link>
                      ))}
                    </section>
                  )}
                  {results.people.length > 0 && (
                    <section>
                      <strong>{en.header.people}</strong>
                      {results.people.map((item) => (
                        <span key={item.id}>{item.name}</span>
                      ))}
                    </section>
                  )}
                </>
              )}
            </div>
          )}
        </div>
        <button className="ui-button ghost" type="button" onClick={signOut}>
          {en.auth.signOut}
        </button>
      </div>
    </header>
  );
}
