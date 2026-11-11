import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { TriangleAlert, CheckCircle, Clock, Phone } from "lucide-react";
import type { components } from "@fleetops/api-client";
import type { DriverSnapshot } from "./storage/snapshot";
import type { DriverShiftState } from "./useShifts";
import { commandQueue, newCommand, type QueuedCommand } from "./storage/commandQueue";
import { compressPhoto, ensurePhotoSpace, photoPayload } from "./storage/photo";
import type { LocalDefectReport } from "./storage/defectReplay";
import { storeEncryptedRecord, decryptRecord, type EncryptedRecord } from "./storage/pinVault";
import { localStore } from "./storage/localStore";
import { companyDate, resolveSuggestedTruck } from "./storage/shiftProjection";
import { useVoiceNote } from "./useVoiceNote";
import { driverDate } from "./DriverSchedule";

const categories: components["schemas"]["DefectCategory"][] = [
  "brakes",
  "lights",
  "tyres",
  "engine",
  "body",
  "tacho",
  "load_securing",
  "other",
];
const severityChoices = [
  { value: "minor", label: "Can drive", icon: CheckCircle },
  { value: "major", label: "Fix soon", icon: Clock },
  { value: "critical", label: "Do not drive", icon: TriangleAlert },
] as const;
type Photo = { file: File; url: string };
const receiptKey = "driver-last-defect";

export function DriverDefect({
  snapshot,
  shifts,
  officePhone,
}: {
  snapshot?: DriverSnapshot;
  shifts: DriverShiftState;
  officePhone: string;
}) {
  const id = useId();
  const running = shifts.shifts.find((shift) => shift.status === "running");
  const defaultVehicle =
    running?.truckId ??
    (shifts.snapshot
      ? resolveSuggestedTruck(
          shifts.snapshot,
          snapshot?.cycles ?? [],
          companyDate(new Date().toISOString(), shifts.snapshot.companyTimeZone),
        )
      : null) ??
    "";
  const [vehicle, setVehicle] = useState("");
  const [category, setCategory] = useState<components["schemas"]["DefectCategory"]>("other");
  const [severity, setSeverity] = useState<components["schemas"]["DefectSeverity"]>("major");
  const [note, setNote] = useState("");
  const voice = useVoiceNote((text) => setNote((previous) => `${previous} ${text}`.trim().slice(0, 4000)));
  const [otherVehicle, setOtherVehicle] = useState(false);
  const [plateSearch, setPlateSearch] = useState("");
  const [photos, setPhotos] = useState<Photo[]>([]);
  const photoUrls = useRef(new Set<string>());
  const [sent, setSent] = useState<QueuedCommand>();
  const [cancelled, setCancelled] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    let active = true;
    void (async () => {
      const record = await localStore.get<EncryptedRecord>(receiptKey);
      const saved = record ? await decryptRecord<QueuedCommand>(record) : undefined;
      const queued = (await commandQueue.list()).filter((command) => command.type === "defect.report").at(-1);
      if (active) setSent(queued && (!saved || queued.deviceTime > saved.deviceTime) ? queued : saved);
    })().catch(() => setError("Saved reports could not be opened. Retry before sending another report."));
    const timer = window.setInterval(() => setNow(Date.now()), 250);
    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, []);
  useEffect(
    () => () => {
      photoUrls.current.forEach((url) => URL.revokeObjectURL(url));
    },
    [],
  );
  useEffect(() => {
    if (!sent) return;
    const update = () => setNow(Date.now());
    const timer = window.setTimeout(update, Math.max(0, Date.parse(sent.deviceTime) + 60_000 - Date.now()));
    window.addEventListener("focus", update);
    document.addEventListener("visibilitychange", update);
    return () => {
      window.clearTimeout(timer);
      window.removeEventListener("focus", update);
      document.removeEventListener("visibilitychange", update);
    };
  }, [sent]);
  const report = sent?.payload as LocalDefectReport | undefined;
  const seconds = sent
    ? Math.min(60, Math.max(0, Math.ceil((Date.parse(sent.deviceTime) + 60_000 - now) / 1000)))
    : 0;
  const cancelCommand = shifts.commands.find(
    (command) => command.type === "defect.cancel" && command.aggregateId === sent?.aggregateId,
  );
  const confirmedCancelled = snapshot?.defects?.some(
    (defect) => defect.id === sent?.aggregateId && defect.status === "cancelled",
  );
  const cancellationRequested = cancelled || !!cancelCommand || confirmedCancelled;
  const choices = new Map((shifts.snapshot?.trucks ?? []).map((truck) => [truck.id, truck.plate]));

  const selected = vehicle || defaultVehicle;
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!selected || (!note.trim() && photos.length === 0)) return;
    setBusy(true);
    setError("");
    try {
      const storedPhotos: LocalDefectReport["photos"] = [];
      let bytes = 0;
      for (const photo of photos) {
        const file = await compressPhoto(photo.file, 0);
        bytes += file.size;
        storedPhotos.push({ page: "page", contentType: file.type, data: await photoPayload(file) });
      }
      await ensurePhotoSpace(bytes);
      const payload: LocalDefectReport = {
        vehicleId: selected,
        category,
        severity,
        description: note.trim() || null,
        photos: storedPhotos,
      };
      const command = newCommand("defect.report", crypto.randomUUID(), 0, payload);
      await commandQueue.enqueue(command);
      setSent(command);
      setCancelled(false);
      setPhotos([]);
      setNote("");
      await storeEncryptedRecord(receiptKey, { ...command, payload: { ...payload, photos: [] } }).catch(() =>
        setError("Report saved. Keep this screen open to cancel a critical report."),
      );
    } catch {
      setError("Your report could not be saved. Keep this screen open and retry.");
    } finally {
      setBusy(false);
    }
  };
  const cancel = async () => {
    if (!sent || Date.now() - Date.parse(sent.deviceTime) > 60_000) {
      setError("Too late to cancel. Call dispatch.");
      return;
    }
    setBusy(true);
    try {
      await commandQueue.enqueue(
        newCommand("defect.cancel", sent.aggregateId, 0, { defectId: sent.aggregateId }),
      );
      setCancelled(true);
    } catch {
      setError("Cancellation could not be saved. Retry or call dispatch.");
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="driver-screen driver-data-screen">
      <h1>Report defect</h1>
      {sent ? (
        <div className="driver-card" role="status">
          <p>Sent. Dispatch will confirm.</p>
          {shifts.commands.some((command) => command.commandId === sent.commandId) && (
            <p>
              <Clock size={16} aria-hidden /> Waiting to sync
            </p>
          )}
          {report?.severity === "critical" && (
            <>
              <p className="driver-error">
                <TriangleAlert size={20} aria-hidden /> Vehicle is off road. Call dispatch
              </p>
              <a className="ui-button primary" href={`tel:${officePhone}`}>
                <Phone size={20} aria-hidden /> Call dispatch
              </a>
              {!cancellationRequested && seconds > 0 && (
                <div>
                  <button disabled={busy} onClick={() => void cancel()}>
                    Cancel report
                  </button>
                  <span> {seconds} s</span>
                </div>
              )}
              {cancellationRequested && (
                <p>
                  {cancelCommand?.state === "failed"
                    ? "Too late to cancel. Call dispatch."
                    : confirmedCancelled
                      ? "Report cancelled."
                      : "Cancellation saved. Waiting to sync."}
                </p>
              )}
            </>
          )}
          <button disabled={busy} onClick={() => setSent(undefined)}>
            Report another defect
          </button>
        </div>
      ) : (
        <form className="driver-sheet" onSubmit={(event) => void submit(event)}>
          <button
            type="button"
            disabled={busy}
            aria-expanded={otherVehicle}
            onClick={() => setOtherVehicle(!otherVehicle)}
          >
            Other vehicle
          </button>
          {otherVehicle && (
            <>
              <label htmlFor={`${id}-search`}>Search vehicle plate (trucks and trailers)</label>
              <input
                id={`${id}-search`}
                type="search"
                value={plateSearch}
                disabled={busy}
                onChange={(event) => setPlateSearch(event.target.value)}
              />
            </>
          )}
          <label htmlFor={`${id}-vehicle`}>Vehicle</label>
          <select
            id={`${id}-vehicle`}
            value={selected}
            required
            disabled={busy}
            onChange={(event) => setVehicle(event.target.value)}
          >
            <option value="">Choose vehicle</option>
            {[...choices]
              .filter(
                ([vehicleId, plate]) =>
                  vehicleId === selected ||
                  !otherVehicle ||
                  plate.toLowerCase().includes(plateSearch.toLowerCase()),
              )
              .map(([vehicleId, plate]) => (
                <option key={vehicleId} value={vehicleId}>
                  {plate} (
                  {shifts.snapshot?.trucks.find((truck) => truck.id === vehicleId)?.kind ?? "vehicle"})
                </option>
              ))}
          </select>
          <fieldset disabled={busy}>
            <legend>Category</legend>
            <div className="driver-defect-choices">
              {categories.map((value) => (
                <button
                  key={value}
                  type="button"
                  aria-pressed={category === value}
                  onClick={() => setCategory(value)}
                >
                  {value === "load_securing" ? "Load securing" : value[0].toUpperCase() + value.slice(1)}
                </button>
              ))}
            </div>
          </fieldset>
          <fieldset disabled={busy}>
            <legend>Severity</legend>
            <div className="driver-defect-choices">
              {severityChoices.map(({ value, label, icon: Icon }) => (
                <button
                  key={value}
                  type="button"
                  aria-pressed={severity === value}
                  onClick={() => setSeverity(value)}
                >
                  <Icon size={18} aria-hidden />
                  {label}
                </button>
              ))}
            </div>
          </fieldset>
          <label htmlFor={`${id}-photos`}>Take photo (up to 5)</label>
          <input
            id={`${id}-photos`}
            type="file"
            accept="image/jpeg,image/png"
            capture="environment"
            multiple
            disabled={busy || photos.length === 5}
            onChange={(event) => {
              const files = Array.from(event.target.files ?? []);
              if (
                files.length + photos.length > 5 ||
                files.some(
                  (file) => !["image/jpeg", "image/png"].includes(file.type) || file.size > 20 * 1024 * 1024,
                )
              ) {
                setError("Choose up to 5 JPEG or PNG photos smaller than 20 MB each.");
                return;
              }
              setPhotos((previous) => [
                ...previous,
                ...files.map((file) => {
                  const url = URL.createObjectURL(file);
                  photoUrls.current.add(url);
                  return { file, url };
                }),
              ]);
              event.target.value = "";
            }}
          />
          <div className="driver-defect-photos">
            {photos.map((photo, index) => (
              <div key={photo.url}>
                <img src={photo.url} alt={`Defect photo ${index + 1}`} />
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => setPhotos((previous) => previous.filter((item) => item !== photo))}
                >
                  Remove photo {index + 1}
                </button>
              </div>
            ))}
          </div>
          <button type="button" disabled={busy || !voice.available || voice.listening} onClick={voice.start}>
            {voice.listening ? "Listening..." : "Voice to text"}
          </button>
          {!voice.available && <p>Use your keyboard microphone to dictate a note.</p>}
          {voice.error && <p role="alert">{voice.error}</p>}
          <label htmlFor={`${id}-note`}>Note (optional)</label>
          <textarea
            id={`${id}-note`}
            value={note}
            maxLength={4000}
            disabled={busy}
            onChange={(event) => setNote(event.target.value)}
          />
          <button
            className="primary"
            type="submit"
            disabled={busy || !selected || (!note.trim() && !photos.length)}
          >
            {busy ? "Saving..." : "Send report"}
          </button>
        </form>
      )}
      {error && (
        <p className="driver-error" role="alert">
          {error}
        </p>
      )}
      <h2>Your reports</h2>
      {snapshot?.defects?.map((defect) => (
        <p key={defect.id}>
          <TriangleAlert size={16} aria-hidden /> {defect.category.replaceAll("_", " ")} {defect.severity}{" "}
          {defect.status.replaceAll("_", " ")} {driverDate(defect.reportedAt)}
        </p>
      ))}
    </section>
  );
}
