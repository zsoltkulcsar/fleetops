import { en } from "../i18n/en";
import { useQueryClient } from "@tanstack/react-query";
import { authApi } from "../auth/api";

export function Header({ pageName }: { pageName: string }) {
  const client = useQueryClient();
  async function signOut() {
    await authApi.signOut();
    client.setQueryData(["session"], null);
  }
  return (
    <header className="topbar">
      <div className="topbar-title">
        <span className="eyebrow">{en.header.workspace}</span>
        <strong>{pageName}</strong>
      </div>
      <div className="topbar-actions">
        <button className="ui-button ghost" type="button" onClick={signOut}>
          {en.auth.signOut}
        </button>
      </div>
    </header>
  );
}
