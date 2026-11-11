import { useCallback, useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { driverApi } from "./api";
import { DriverShell } from "./DriverShell";
import { InstallGuide, isStandalone } from "./InstallGuide";
import { PinForm } from "./PinForm";
import { en } from "./i18n/en";
import { localStore } from "./storage/localStore";
import { commandQueue } from "./storage/commandQueue";
import { resetSessionState } from "./sessionEvents";
import { resetVaultPin } from "./storage/resetVaultPin";
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
  | "reset-pin"
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
  const [employeeStatus, setEmployeeStatus] = useState<string>();
  const [unsent, setUnsent] = useState<string[]>([]);
  const config = useQuery({
    queryKey: ["driver-config"],
    queryFn: driverApi.config,
    enabled: stage === "set-pin" || stage === "reset-pin",
    retry: false,
  });

  const load = useCallback(async () => {
    try {
      const revokedPhone = await localStore.get<string>("revoked");
      if (revokedPhone !== undefined) {
        lockVault();
        setOfficePhone(revokedPhone);
        setStage("revoked");
        setUnsent((await localStore.get<string[]>("revoked-unsent")) ?? []);
        return;
      }
      const stored = await readVault();
      setVault(stored);
      if (stored) {
        setEmployeeStatus(stored.config.employeeStatus);
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
      if ((await localStore.get<string>("revoked")) === undefined) setStage("expired");
    } else if (status === 200) {
      const fresh = await driverApi.config();
      setEmployeeStatus(fresh.employeeStatus);
      if (fresh.pinResetRequired) {
        client.setQueryData(["driver-config"], fresh);
        setStage("reset-pin");
        return false;
      }
      await markOnline(fresh);
      const current = await readVault();
      setVault(current);
      if (current && !current.locked) setStage(isUnlocked() ? "shell" : "unlock");
      return true;
    }
  }, [officePhone, client]);

  useEffect(() => {
    const suspend = () => setEmployeeStatus("suspended");
    const revoke = () => {
      void (async () => {
        let items: string[];
        try {
          items = (await commandQueue.list()).map(
            (command) =>
              `${command.type.replaceAll(".", " ")} - ${new Date(command.deviceTime).toLocaleString("de-DE")}`,
          );
        } catch {
          items = ["Saved items could not be opened on this locked phone."];
        }
        setUnsent(items);
        await localStore.set("revoked-unsent", items);
        await localStore.set("revoked", officePhone);
        lockVault();
        client.clear();
        setStage("revoked");
      })().catch(() => {
        lockVault();
        setStage("revoked");
      });
    };
    window.addEventListener("fleetops:driver-revoked", revoke);
    window.addEventListener("fleetops:driver-suspended", suspend);
    return () => {
      window.removeEventListener("fleetops:driver-revoked", revoke);
      window.removeEventListener("fleetops:driver-suspended", suspend);
    };
  }, [officePhone, client]);

  useEffect(() => {
    if (!online || !["shell", "locked", "unlock", "expired"].includes(stage)) return;
    const check = () => {
      void checkSession().catch(() => {
        /* Keep the local screen when the connection fails. */
      });
    };
    check();
    const timer = window.setInterval(check, 15_000);
    return () => window.clearInterval(timer);
  }, [online, stage, checkSession]);

  useEffect(() => {
    const expiredSession = () => {
      void checkSession();
    };
    window.addEventListener("fleetops:driver-session-expired", expiredSession);
    return () => window.removeEventListener("fleetops:driver-session-expired", expiredSession);
  }, [checkSession]);

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
        <label htmlFor="enrol-personnel-number">
          {en.personnelNumber}
          <input
            id="enrol-personnel-number"
            name="personnelNumber"
            autoComplete="username"
            required
            disabled={enrol.isPending}
          />
        </label>
        <label htmlFor="enrol-code">
          {en.smsCode}
          <input
            id="enrol-code"
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
  else if ((stage === "set-pin" || stage === "reset-pin") && !config.data)
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
  else if (stage === "set-pin" || stage === "reset-pin" || stage === "unlock")
    content = (
      <PinForm
        key={stage}
        setting={stage !== "unlock"}
        length={(stage !== "unlock" ? config.data?.pinLength : vault?.config.pinLength) ?? 0}
        busy={busy}
        error={error}
        onSubmit={(pin) =>
          run(async () => {
            if (stage === "set-pin" || stage === "reset-pin") {
              if (!config.data) throw new Error("missing_config");
              if (stage === "reset-pin") {
                await resetVaultPin(pin, config.data);
                await driverApi.completePinReset();
              } else await createVault(pin, config.data);
              sessionStorage.removeItem("driver-onboarding");
              setOfficePhone(config.data.officePhone);
              setEmployeeStatus(config.data.employeeStatus);
              setVault(await readVault());
              setStage("shell");
            } else {
              const result = await unlock(pin);
              const current = await readVault();
              setVault(current);
              if (result === "unlocked") {
                if ((await checkSession()) && isUnlocked()) setStage("shell");
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
        <h1>{stage === "revoked" ? "This phone was signed out. Not sent:" : en[stage]}</h1>
        {stage === "revoked" && (
          <>
            <ul>
              {unsent.map((item, index) => (
                <li key={index}>{item}</li>
              ))}
            </ul>
            {unsent.length === 0 && <p>No unsent items.</p>}
            <button
              className="primary"
              disabled={busy}
              onClick={() =>
                void run(async () => {
                  lockVault();
                  await Promise.allSettled([
                    eraseVault(),
                    localStore.clear(),
                    (async () => {
                      if ("caches" in window) {
                        for (const key of await caches.keys())
                          if (key.includes("fleetops") || key.includes("workbox")) await caches.delete(key);
                      }
                    })(),
                  ]);
                  sessionStorage.removeItem("driver-onboarding");
                  client.clear();
                  setVault(undefined);
                  setUnsent([]);
                  resetSessionState();
                  setStage("enrol");
                })
              }
            >
              OK
            </button>
          </>
        )}
        <div className="driver-actions">
          {stage === "expired" && (
            <button
              className="primary"
              disabled={!online || busy}
              onClick={() =>
                void run(async () => {
                  await checkSession();
                })
              }
            >
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
        employeeStatus={employeeStatus}
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
