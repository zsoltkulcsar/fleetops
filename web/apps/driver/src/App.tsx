import { useCallback, useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { driverApi } from "./api";
import { DriverShell } from "./DriverShell";
import { InstallGuide, isStandalone } from "./InstallGuide";
import { PinForm } from "./PinForm";
import { en } from "./i18n/en";
import { localStore } from "./storage/localStore";
import { commandQueue } from "./storage/commandQueue";
import {
  createVault,
  eraseVault,
  expired,
  isUnlocked,
  lockVault,
  markOnline,
  readVault,
  unlock,
  type Vault,
} from "./storage/pinVault";

type Stage =
  | "loading"
  | "enrol"
  | "privacy"
  | "set-pin"
  | "unlock"
  | "locked"
  | "expired"
  | "revoked"
  | "shell"
  | "storage-error";

export function App() {
  const client = useQueryClient();
  const [stage, setStage] = useState<Stage>("loading");
  const [vault, setVault] = useState<Vault>();
  const [officePhone, setOfficePhone] = useState("");
  const [online, setOnline] = useState(navigator.onLine);
  const [install, setInstall] = useState(false);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const config = useQuery({
    queryKey: ["driver-config"],
    queryFn: driverApi.config,
    enabled: stage === "set-pin",
    retry: false,
  });

  const load = useCallback(async () => {
    try {
      const revokedPhone = await localStore.get<string>("revoked");
      if (revokedPhone !== undefined) {
        lockVault();
        setOfficePhone(revokedPhone);
        setStage("revoked");
        return;
      }
      const stored = await readVault();
      setVault(stored);
      if (stored) {
        setOfficePhone(stored.config.officePhone);
        setStage(
          stored.locked || !stored.wrappedKey || stored.failedTries >= stored.config.maxPinTries
            ? "locked"
            : expired(stored)
              ? "expired"
              : "unlock",
        );
      } else {
        const pending = sessionStorage.getItem("driver-onboarding");
        setStage(pending === "privacy" ? "privacy" : pending === "set-pin" ? "set-pin" : "enrol");
      }
      setInstall(!isStandalone() && !(await localStore.get<boolean>("install-dismissed")));
    } catch {
      setStage("storage-error");
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);
  useEffect(() => {
    const update = () => setOnline(navigator.onLine);
    window.addEventListener("online", update);
    window.addEventListener("offline", update);
    return () => {
      window.removeEventListener("online", update);
      window.removeEventListener("offline", update);
    };
  }, []);

  const checkSession = useCallback(async () => {
    if (!navigator.onLine) return;
    let status: number;
    try {
      status = await driverApi.session();
    } catch {
      return;
    }
    if (status === 401) {
      lockVault();
      // Persist the safe contact separately so a reload cannot restore access.
      await localStore.set("revoked", officePhone);
      await eraseVault();
      client.clear();
      sessionStorage.removeItem("driver-onboarding");
      setVault(undefined);
      setStage("revoked");
    } else if (status === 200) {
      await markOnline();
      const current = await readVault();
      setVault(current);
      if (current && !current.locked) setStage(isUnlocked() ? "shell" : "unlock");
    }
  }, [officePhone, client]);

  useEffect(() => {
    const expiredSession = () => {
      void checkSession();
    };
    window.addEventListener("fleetops:driver-session-expired", expiredSession);
    return () => window.removeEventListener("fleetops:driver-session-expired", expiredSession);
  }, [checkSession]);

  useEffect(() => {
    if (online && (stage === "shell" || stage === "expired")) {
      void checkSession().catch(() => {
        lockVault();
        setStage("storage-error");
      });
    }
  }, [online, stage, checkSession]);

  useEffect(() => {
    if (stage !== "shell" || !vault) return;
    const check = async () => {
      const current = await readVault();
      if (!current || current.locked || expired(current)) {
        lockVault();
        await load();
      }
    };
    const timer = window.setInterval(() => {
      void check().catch(() => {
        lockVault();
        setStage("storage-error");
      });
    }, 15_000);
    const expiresIn = vault.lastOnlineAt + vault.config.maxOfflineDays * 86_400_000 - Date.now();
    const expiryTimer = window.setTimeout(
      () => {
        void check().catch(() => {
          lockVault();
          setStage("storage-error");
        });
      },
      Math.max(0, Math.min(expiresIn + 1, 2_147_483_647)),
    );
    return () => {
      window.clearInterval(timer);
      window.clearTimeout(expiryTimer);
    };
  }, [stage, vault, load]);

  const dismissInstall = useCallback(() => {
    void localStore
      .set("install-dismissed", true)
      .then(() => setInstall(false))
      .catch(() => setStage("storage-error"));
  }, []);

  const enrol = useMutation({
    mutationFn: ({ personnelNumber, code }: { personnelNumber: string; code: string }) =>
      driverApi.enrol(personnelNumber, code),
    onSuccess: async (result) => {
      if (result.stage !== "pending_privacy" && result.stage !== "full") {
        setError(en.enrolFailure);
        return;
      }
      const next = result.stage === "pending_privacy" ? "privacy" : "set-pin";
      sessionStorage.setItem("driver-onboarding", next);
      if (navigator.storage?.persist) {
        try {
          await navigator.storage.persist();
        } catch {
          /* Persistence is a best-effort browser request. */
        }
      }
      setError("");
      setStage(next);
    },
    onError: () => setError(en.enrolFailure),
  });

  const run = async (operation: () => Promise<void>) => {
    setBusy(true);
    setError("");
    try {
      await operation();
    } catch {
      setError(en.error);
    } finally {
      setBusy(false);
    }
  };

  let content;
  if (stage === "loading") content = <p role="status">{en.loading}</p>;
  else if (stage === "storage-error")
    content = (
      <section className="driver-screen">
        <h1>{en.storageError}</h1>
        <button onClick={() => void load()}>{en.retry}</button>
      </section>
    );
  else if (install) content = <InstallGuide onContinue={dismissInstall} />;
  else if (stage === "enrol")
    content = (
      <form
        className="driver-screen"
        onSubmit={(event) => {
          event.preventDefault();
          const fields = new FormData(event.currentTarget);
          enrol.mutate({
            personnelNumber: String(fields.get("personnelNumber")),
            code: String(fields.get("code")),
          });
          event.currentTarget.reset();
        }}
      >
        <h1>{en.enrol}</h1>
        <label>
          {en.personnelNumber}
          <input name="personnelNumber" autoComplete="username" required disabled={enrol.isPending} />
        </label>
        <label>
          {en.smsCode}
          <input
            name="code"
            inputMode="numeric"
            autoComplete="one-time-code"
            pattern="[0-9]+"
            required
            disabled={enrol.isPending}
          />
        </label>
        <div className="driver-actions">
          <button className="primary" disabled={enrol.isPending || !online}>
            {enrol.isPending ? en.working : en.continue}
          </button>
        </div>
      </form>
    );
  else if (stage === "privacy")
    content = (
      <section className="driver-screen">
        <h1>{en.privacy}</h1>
        <p>{en.privacyBody}</p>
        <div className="driver-actions">
          <button
            className="primary"
            disabled={busy || !online}
            onClick={() =>
              void run(async () => {
                await driverApi.privacy();
                sessionStorage.setItem("driver-onboarding", "set-pin");
                setStage("set-pin");
              })
            }
          >
            {busy ? en.working : en.acknowledge}
          </button>
        </div>
      </section>
    );
  else if (stage === "set-pin" && !config.data)
    content = (
      <section className="driver-screen">
        <h1>{en.setPin}</h1>
        <p role="status">{config.isError ? en.error : en.loading}</p>
        {config.isError && (
          <button disabled={!online} onClick={() => void config.refetch()}>
            {en.retry}
          </button>
        )}
      </section>
    );
  else if (stage === "set-pin" || stage === "unlock")
    content = (
      <PinForm
        key={stage}
        setting={stage === "set-pin"}
        length={(stage === "set-pin" ? config.data?.pinLength : vault?.config.pinLength) ?? 0}
        busy={busy}
        error={error}
        onSubmit={(pin) =>
          run(async () => {
            if (stage === "set-pin") {
              if (!config.data) throw new Error("missing_config");
              await createVault(pin, config.data);
              sessionStorage.removeItem("driver-onboarding");
              setOfficePhone(config.data.officePhone);
              setVault(await readVault());
              setStage("shell");
            } else {
              const result = await unlock(pin);
              const current = await readVault();
              setVault(current);
              if (result === "unlocked") {
                await checkSession();
                if (isUnlocked()) setStage("shell");
              } else if (result === "wrong" && current) {
                setError(en.triesLeft(current.config.maxPinTries - current.failedTries));
              } else setStage(result === "missing" ? "enrol" : result === "expired" ? "expired" : "locked");
            }
          })
        }
      />
    );
  else if (stage === "locked" || stage === "expired" || stage === "revoked")
    content = (
      <section className="driver-screen">
        <h1>{en[stage]}</h1>
        <div className="driver-actions">
          {stage === "expired" && (
            <button className="primary" disabled={!online || busy} onClick={() => void run(checkSession)}>
              {en.retry}
            </button>
          )}
          {officePhone && (
            <a className="driver-button" href={`tel:${officePhone}`}>
              {en.callOffice}
            </a>
          )}
        </div>
      </section>
    );
  else if (stage === "shell" && vault)
    content = (
      <DriverShell
        name={vault.config.name}
        officePhone={vault.config.officePhone}
        busy={busy}
        onSignOut={() =>
          run(async () => {
            await navigator.locks.request("fleetops-driver-command-queue", async () => {
              if ((await commandQueue.list()).length > 0) {
                setError(
                  "Sync your saved work before signing out. Signing out would remove it from this device.",
                );
                return;
              }
              await driverApi.signOut();
              await eraseVault();
              client.clear();
              sessionStorage.removeItem("driver-onboarding");
              setVault(undefined);
              setStage("enrol");
            });
          })
        }
      />
    );

  return (
    <div className="driver-app">
      <a className="skip-link" href="#content">
        {en.skip}
      </a>
      <header className="driver-header">
        <span>{en.appName}</span>
        {!online && (
          <span className="offline-pill" role="status">
            {en.offline}
          </span>
        )}
      </header>
      <main id="content">
        {content}
        {error && stage !== "unlock" && stage !== "set-pin" && (
          <p className="driver-error" role="alert">
            {error}
          </p>
        )}
      </main>
    </div>
  );
}
