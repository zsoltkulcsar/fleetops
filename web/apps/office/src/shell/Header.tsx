import { en } from "../i18n/en";

export function Header({ pageName }: { pageName: string }) {
  return (
    <header className="topbar">
      <div className="topbar-title">
        <span className="eyebrow">{en.header.workspace}</span>
        <strong>{pageName}</strong>
      </div>
      <div className="topbar-actions" />
    </header>
  );
}
