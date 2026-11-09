import { useState } from "react";
import { en } from "./i18n/en";

export function PinForm({
  length,
  setting,
  busy,
  error,
  onSubmit,
}: {
  length: number;
  setting: boolean;
  busy: boolean;
  error: string;
  onSubmit: (pin: string) => Promise<void>;
}) {
  const [pin, setPin] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [mismatch, setMismatch] = useState(false);
  return (
    <form
      className="driver-screen"
      onSubmit={async (event) => {
        event.preventDefault();
        if (setting && pin !== confirmation) {
          setMismatch(true);
          return;
        }
        const submittedPin = pin;
        setPin("");
        setConfirmation("");
        setMismatch(false);
        await onSubmit(submittedPin);
      }}
    >
      <h1>{setting ? en.setPin : en.unlock}</h1>
      {setting && <p id="pin-hint">{en.pinHint(length)}</p>}
      <label>
        {en.pin}
        <input
          name="pin"
          type="password"
          inputMode="numeric"
          autoComplete="off"
          pattern={`[0-9]{${length}}`}
          minLength={length}
          maxLength={length}
          required
          aria-describedby={setting ? "pin-hint" : undefined}
          value={pin}
          disabled={busy}
          onChange={(event) => setPin(event.target.value)}
        />
      </label>
      {setting && (
        <label>
          {en.confirmPin}
          <input
            name="confirmPin"
            type="password"
            inputMode="numeric"
            autoComplete="off"
            pattern={`[0-9]{${length}}`}
            minLength={length}
            maxLength={length}
            required
            value={confirmation}
            disabled={busy}
            onChange={(event) => setConfirmation(event.target.value)}
          />
        </label>
      )}
      {(mismatch || error) && (
        <p className="driver-error" role="alert">
          {mismatch ? en.pinMismatch : error}
        </p>
      )}
      <div className="driver-actions">
        <button className="primary" disabled={busy}>
          {busy ? en.working : setting ? en.continue : en.unlockButton}
        </button>
      </div>
    </form>
  );
}
