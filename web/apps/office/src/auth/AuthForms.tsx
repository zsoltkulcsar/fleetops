import { useState, type FormEvent, type ReactNode } from "react";
import { Link } from "react-router";
import { Banner } from "@fleetops/ui";
import { authApi, AuthRequestError, type NextStep } from "./api";
import { en } from "../i18n/en";
import { ExpiredNotice } from "./ExpiredNotice";

type Props = { onNext: (next: NextStep) => void; expired?: boolean };
function AuthFrame({
  title,
  children,
  error,
  expired,
}: {
  title: string;
  children: ReactNode;
  error?: string;
  expired?: boolean;
}) {
  return (
    <main className="auth-page">
      <div className="auth-card">
        <div className="brand">
          <span className="brand-mark">F</span>
          <span>FleetOps</span>
        </div>
        <h1>{title}</h1>
        {expired && <ExpiredNotice />}
        {error && <Banner label={en.banner.error}>{error}</Banner>}
        {children}
      </div>
    </main>
  );
}
function useSubmit() {
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  return { error, setError, busy, setBusy };
}

export function SignIn({ onNext, expired }: Props) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const form = useSubmit();
  async function submit(event: FormEvent) {
    event.preventDefault();
    form.setBusy(true);
    form.setError("");
    try {
      const result = await authApi.signIn(email, password);
      onNext(result.next);
    } catch (error) {
      const authError = error as AuthRequestError;
      form.setError(authError.problem.code === "account_locked" ? en.auth.locked : en.auth.wrongDetails);
    } finally {
      form.setBusy(false);
    }
  }
  return (
    <AuthFrame title={en.auth.signIn} expired={expired} error={form.error}>
      <form onSubmit={submit} className="auth-form">
        <label>
          {en.auth.email}
          <input
            required
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            autoComplete="username"
          />
        </label>
        <label>
          {en.auth.password}
          <input
            required
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            autoComplete="current-password"
          />
        </label>
        <button className="ui-button primary" disabled={form.busy}>
          {en.auth.continue}
        </button>
        <Link to="?forgot=1" className="auth-link">
          {en.auth.forgotPassword}
        </Link>
      </form>
    </AuthFrame>
  );
}

export function SecondFactor({ onNext }: Props) {
  const [code, setCode] = useState("");
  const form = useSubmit();
  async function submit(event: FormEvent) {
    event.preventDefault();
    form.setBusy(true);
    form.setError("");
    try {
      onNext((await authApi.secondFactor(code)).next);
    } catch {
      form.setError(en.auth.secondFactorFailed);
    } finally {
      form.setBusy(false);
    }
  }
  return (
    <AuthFrame title={en.auth.secondFactor} error={form.error}>
      <p className="ui-muted">{en.auth.secondFactorBody}</p>
      <form onSubmit={submit} className="auth-form">
        <label>
          Code
          <input
            required
            inputMode="numeric"
            pattern="[0-9]{6}"
            value={code}
            onChange={(event) => setCode(event.target.value)}
            autoComplete="one-time-code"
          />
        </label>
        <button className="ui-button primary" disabled={form.busy}>
          {en.auth.continue}
        </button>
      </form>
    </AuthFrame>
  );
}
export function Enrol({ onNext }: Props) {
  const [details, setDetails] = useState<{ secret: string; otpauthUri: string }>();
  const [code, setCode] = useState("");
  const form = useSubmit();
  async function load() {
    try {
      setDetails(await authApi.enrol());
    } catch {
      form.setError(en.auth.secondFactorFailed);
    }
  }
  if (!details)
    return (
      <AuthFrame title={en.auth.enrol} error={form.error}>
        <p>{en.auth.enrolBody}</p>
        <button className="ui-button primary" onClick={load}>
          {en.auth.continue}
        </button>
      </AuthFrame>
    );
  async function submit(event: FormEvent) {
    event.preventDefault();
    form.setBusy(true);
    try {
      onNext((await authApi.secondFactor(code)).next);
    } catch {
      form.setError(en.auth.secondFactorFailed);
    } finally {
      form.setBusy(false);
    }
  }
  return (
    <AuthFrame title={en.auth.enrol} error={form.error}>
      <p>{en.auth.enrolBody}</p>
      <p>
        <strong>{en.auth.secret}:</strong> <code>{details.secret}</code>
      </p>
      <p className="ui-muted">{details.otpauthUri}</p>
      <form onSubmit={submit} className="auth-form">
        <label>
          Code
          <input
            required
            inputMode="numeric"
            value={code}
            onChange={(event) => setCode(event.target.value)}
          />
        </label>
        <button className="ui-button primary">{en.auth.continue}</button>
      </form>
    </AuthFrame>
  );
}
export function PrivacyNotice({ onNext }: Props) {
  const [checked, setChecked] = useState(false);
  const form = useSubmit();
  async function submit(event: FormEvent) {
    event.preventDefault();
    form.setBusy(true);
    try {
      onNext((await authApi.privacy("1")).next);
    } catch {
      form.setError(en.auth.wrongDetails);
    } finally {
      form.setBusy(false);
    }
  }
  return (
    <AuthFrame title={en.auth.privacy} error={form.error}>
      <p>{en.auth.privacyBody}</p>
      <form onSubmit={submit} className="auth-form">
        <label className="check-row">
          <input type="checkbox" checked={checked} onChange={(event) => setChecked(event.target.checked)} />
          {en.auth.acknowledge}
        </label>
        <button className="ui-button primary" disabled={!checked || form.busy}>
          {en.auth.continue}
        </button>
      </form>
    </AuthFrame>
  );
}
export function Forgot() {
  const [email, setEmail] = useState("");
  const [sent, setSent] = useState(false);
  async function submit(event: FormEvent) {
    event.preventDefault();
    await authApi.forgot(email);
    setSent(true);
  }
  return (
    <AuthFrame title={en.auth.forgot}>
      {sent ? (
        <>
          <Banner kind="info" label={en.banner.info}>
            {en.auth.forgotConfirmation}
          </Banner>
          <Link className="auth-link" to=".">
            {en.auth.backToSignIn}
          </Link>
        </>
      ) : (
        <form onSubmit={submit} className="auth-form">
          <p>{en.auth.forgotBody}</p>
          <label>
            {en.auth.email}
            <input required type="email" value={email} onChange={(event) => setEmail(event.target.value)} />
          </label>
          <button className="ui-button primary">{en.auth.continue}</button>
          <Link className="auth-link" to=".">
            {en.auth.backToSignIn}
          </Link>
        </form>
      )}
    </AuthFrame>
  );
}
export function Reset() {
  const token = new URLSearchParams(window.location.search).get("token") ?? "";
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [done, setDone] = useState(false);
  async function submit(event: FormEvent) {
    event.preventDefault();
    try {
      await authApi.reset(token, password);
      setDone(true);
    } catch {
      setError(en.auth.resetFailed);
    }
  }
  return (
    <AuthFrame title={en.auth.reset} error={error}>
      {done ? (
        <Link className="auth-link" to=".">
          {en.auth.backToSignIn}
        </Link>
      ) : (
        <form onSubmit={submit} className="auth-form">
          <label>
            {en.auth.password}
            <input
              required
              minLength={12}
              type="password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </label>
          <button className="ui-button primary">{en.auth.continue}</button>
          <Link className="auth-link" to=".">
            {en.auth.backToSignIn}
          </Link>
        </form>
      )}
    </AuthFrame>
  );
}
