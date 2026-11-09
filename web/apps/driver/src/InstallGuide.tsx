import { useEffect, useState } from "react";
import { en } from "./i18n/en";

interface InstallPrompt extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
}

export function InstallGuide({ onContinue }: { onContinue: () => void }) {
  const [prompt, setPrompt] = useState<InstallPrompt>();
  const [busy, setBusy] = useState(false);
  const iphone =
    /iPhone|iPad|iPod/.test(navigator.userAgent) ||
    (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  useEffect(() => {
    const capture = (event: Event) => {
      event.preventDefault();
      setPrompt(event as InstallPrompt);
    };
    window.addEventListener("beforeinstallprompt", capture);
    window.addEventListener("appinstalled", onContinue);
    return () => {
      window.removeEventListener("beforeinstallprompt", capture);
      window.removeEventListener("appinstalled", onContinue);
    };
  }, [onContinue]);
  return (
    <section className="driver-screen">
      <h1>{en.install}</h1>
      <ol className="install-steps">
        {(iphone ? en.iphoneSteps : en.browserSteps).map((step) => (
          <li key={step}>{step}</li>
        ))}
      </ol>
      <div className="driver-actions">
        {!iphone && prompt && (
          <button
            className="primary"
            disabled={busy}
            onClick={async () => {
              setBusy(true);
              try {
                await prompt.prompt();
                if ((await prompt.userChoice).outcome === "accepted") onContinue();
              } finally {
                setPrompt(undefined);
                setBusy(false);
              }
            }}
          >
            {en.installApp}
          </button>
        )}
        <button onClick={onContinue}>{en.notNow}</button>
      </div>
    </section>
  );
}

export const isStandalone = () =>
  window.matchMedia("(display-mode: standalone)").matches ||
  (navigator as Navigator & { standalone?: boolean }).standalone === true;
