import { useEffect, useState, type FormEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import { Drawer } from "@fleetops/ui";
import type { components } from "@fleetops/api-client";
import type { DriverSnapshot } from "./storage/snapshot";
import { driverDate } from "./DriverSchedule";
import { DriverRequestError, scheduleApi } from "./scheduleApi";
import { openFreshFile } from "./DriverTruck";

type DocumentType = components["schemas"]["DocumentType"];
const documentTypes: { value: DocumentType; label: string }[] = [
  { value: "licence_c_ce", label: "Driving licence" },
  { value: "driver_card", label: "Driver card" },
  { value: "code95", label: "Code 95" },
  { value: "medical", label: "Medical" },
  { value: "adr_certificate", label: "ADR certificate" },
  { value: "id_card", label: "Identity card" },
];
export const documentLabel = (type: string) =>
  documentTypes.find((item) => item.value === type)?.label ?? type.replaceAll("_", " ");

export function DriverDocuments({
  snapshot,
  online,
  onRefresh,
  onSignOut,
  busy,
}: {
  snapshot?: DriverSnapshot;
  online: boolean;
  onRefresh: () => Promise<void>;
  onSignOut: () => Promise<void>;
  busy: boolean;
}) {
  const [opened, setOpened] = useState<string>();
  const [uploadType, setUploadType] = useState<DocumentType>();
  const [sent, setSent] = useState(false);
  const waiting = new Set(
    snapshot?.documents.filter((document) => document.status === "uploaded").map((document) => document.type),
  );
  return (
    <section className="driver-screen driver-data-screen">
      <h1>Me</h1>
      <h2>Your documents</h2>
      {sent && (
        <p className="driver-callout" role="status">
          Sent to HR. Awaiting verification.
        </p>
      )}
      {snapshot?.documents.length ? (
        <ul className="driver-document-list">
          {snapshot.documents.map((document) => (
            <li key={document.id}>
              <div>
                <strong>{documentLabel(document.type)}</strong>
                <span className="driver-badge">
                  {document.status === "uploaded" ? "Awaiting verification" : document.status}
                </span>
                <span>
                  {document.expiresAt
                    ? `Expires ${driverDate(document.expiresAt)}`
                    : "No expiry date recorded"}
                </span>
                {document.status === "rejected" && (
                  <p className="driver-error">{document.rejectReason ?? "Ask HR about this document."}</p>
                )}
                <button disabled={!online} onClick={() => setOpened(document.id)}>
                  View document
                </button>
                {document.status !== "uploaded" && document.status !== "replaced" && (
                  <button
                    disabled={!online || waiting.has(document.type)}
                    onClick={() => setUploadType(document.type)}
                  >
                    {document.status === "rejected" ? "Upload again" : "Upload renewal"}
                  </button>
                )}
              </div>
            </li>
          ))}
        </ul>
      ) : (
        <p>
          {snapshot
            ? "No documents recorded yet. Upload a photo to send it to HR."
            : "Connect and refresh your schedule to see your documents."}
        </p>
      )}
      <button disabled={!online} onClick={() => setUploadType("licence_c_ce")}>
        Upload a document
      </button>
      {!online && (
        <p className="driver-sync">
          Document files and uploads need a connection. Your saved statuses remain available.
        </p>
      )}
      <div className="driver-actions">
        <button
          disabled={busy}
          onClick={async () => {
            if (
              window.confirm(
                "Sign out and remove this device’s PIN and saved schedule? You will need a new enrolment code to return.",
              )
            )
              await onSignOut();
          }}
        >
          {busy ? "Please wait…" : "Sign out"}
        </button>
      </div>
      {opened && (
        <DocumentDetail
          id={opened}
          online={online}
          onClose={() => setOpened(undefined)}
          onUpload={(type) => {
            setOpened(undefined);
            setUploadType(type);
          }}
        />
      )}
      {uploadType && (
        <UploadDocument
          initialType={uploadType}
          online={online}
          waiting={waiting}
          onClose={() => setUploadType(undefined)}
          onSaved={async () => {
            setUploadType(undefined);
            setSent(true);
            await onRefresh();
          }}
        />
      )}
    </section>
  );
}

function DocumentDetail({
  id,
  online,
  onClose,
  onUpload,
}: {
  id: string;
  online: boolean;
  onClose: () => void;
  onUpload: (type: DocumentType) => void;
}) {
  const detail = useQuery({
    queryKey: ["driver-document", id],
    queryFn: () => scheduleApi.document(id),
    enabled: online,
    gcTime: 0,
  });
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const openFile = async (fileId: string) => {
    setBusy(true);
    setError("");
    try {
      await openFreshFile(id, fileId);
    } catch {
      setError("This file could not be opened. Connect and try again.");
    } finally {
      setBusy(false);
    }
  };
  return (
    <Drawer
      open
      title={detail.data ? documentLabel(detail.data.type) : "Your document"}
      closeLabel="Close document"
      onClose={onClose}
    >
      <div className="driver-sheet">
        {!online ? (
          <p>Connect to view this file.</p>
        ) : detail.isPending ? (
          <p role="status">Loading document…</p>
        ) : detail.isError ? (
          <p role="alert">This document is unavailable. Close and try again.</p>
        ) : (
          detail.data && (
            <>
              <span className="driver-badge">
                {detail.data.status === "uploaded" ? "Awaiting verification" : detail.data.status}
              </span>
              <p>
                {detail.data.expiresAt
                  ? `Expires ${driverDate(detail.data.expiresAt)}`
                  : "No expiry date recorded"}
              </p>
              {detail.data.status === "rejected" && (
                <>
                  <p className="driver-error" role="alert">
                    {detail.data.rejectReason}
                  </p>
                  <button onClick={() => onUpload(detail.data!.type)}>Upload again</button>
                </>
              )}
              {detail.data.files.map((file) => (
                <button disabled={busy} key={file.id} onClick={() => void openFile(file.id)}>
                  Open {file.page === "page" ? "file" : file.page}
                </button>
              ))}
            </>
          )
        )}
        {error && (
          <p className="driver-error" role="alert">
            {error}
          </p>
        )}
      </div>
    </Drawer>
  );
}

type Photo = { file: File; url: string; crop: number };
function PhotoInput({
  side,
  photo,
  onPhoto,
}: {
  side: "front" | "back";
  photo?: Photo;
  onPhoto: (photo?: Photo) => void;
}) {
  const [error, setError] = useState("");
  useEffect(
    () => () => {
      if (photo) URL.revokeObjectURL(photo.url);
    },
    [photo?.url],
  );
  return (
    <div className="driver-photo">
      <label>
        {side === "front" ? "Front photo" : "Back photo"}
        <input
          type="file"
          accept="image/jpeg,image/png"
          capture="environment"
          onChange={(event) => {
            const file = event.target.files?.[0];
            setError("");
            if (!file) return;
            if (!["image/jpeg", "image/png"].includes(file.type) || file.size > 20 * 1024 * 1024) {
              setError("Choose a JPEG or PNG photo smaller than 20 MB.");
              event.target.value = "";
              return;
            }
            onPhoto({ file, url: URL.createObjectURL(file), crop: 0 });
          }}
        />
      </label>
      {photo && (
        <>
          <div className="driver-photo-preview">
            <img
              src={photo.url}
              width={300}
              height={200}
              alt={`${side === "front" ? "Front" : "Back"} document preview`}
              style={{ clipPath: `inset(${photo.crop}%)` }}
            />
          </div>
          <label>
            Crop edges
            <input
              type="range"
              min={0}
              max={20}
              step={1}
              value={photo.crop}
              onChange={(event) => onPhoto({ ...photo, crop: Number(event.target.value) })}
            />
          </label>
          <button type="button" onClick={() => onPhoto(undefined)}>
            Remove {side} photo
          </button>
        </>
      )}
      {error && (
        <p className="driver-error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
async function cropped(photo: Photo): Promise<Blob> {
  if (photo.crop === 0) return photo.file;
  const bitmap = await createImageBitmap(photo.file);
  try {
    const insetX = Math.round((bitmap.width * photo.crop) / 100);
    const insetY = Math.round((bitmap.height * photo.crop) / 100);
    const canvas = document.createElement("canvas");
    canvas.width = bitmap.width - insetX * 2;
    canvas.height = bitmap.height - insetY * 2;
    const context = canvas.getContext("2d");
    if (!context) throw new Error("image_unavailable");
    context.drawImage(bitmap, insetX, insetY, canvas.width, canvas.height, 0, 0, canvas.width, canvas.height);
    return await new Promise<Blob>((resolve, reject) =>
      canvas.toBlob(
        (blob) => (blob ? resolve(blob) : reject(new Error("image_unavailable"))),
        "image/jpeg",
        0.9,
      ),
    );
  } finally {
    bitmap.close();
  }
}
function UploadDocument({
  initialType,
  online,
  waiting,
  onClose,
  onSaved,
}: {
  initialType: DocumentType;
  online: boolean;
  waiting: Set<DocumentType>;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const [type, setType] = useState(initialType);
  const [front, setFront] = useState<Photo>();
  const [back, setBack] = useState<Photo>();
  const [twoSides, setTwoSides] = useState(["licence_c_ce", "driver_card", "id_card"].includes(initialType));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [progress, setProgress] = useState("");
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!front || !online || waiting.has(type) || (twoSides && !back)) return;
    const form = new FormData(event.currentTarget);
    setBusy(true);
    setError("");
    try {
      const confirmed: components["schemas"]["ConfirmFileInput"][] = [];
      const photos = [
        { photo: front, page: "front" as const },
        ...(back && twoSides ? [{ photo: back, page: "back" as const }] : []),
      ];
      for (const [index, item] of photos.entries()) {
        setProgress(`Sending photo ${index + 1} of ${photos.length}…`);
        const file = await cropped(item.photo);
        const grant = await scheduleApi.upload({ type, contentType: file.type, sizeBytes: file.size });
        const upload = await fetch(grant.url.url, {
          method: "PUT",
          headers: grant.requiredHeaders,
          body: file,
          credentials: "omit",
        });
        if (!upload.ok) throw new Error("upload_failed");
        confirmed.push({ fileId: grant.fileId, page: item.page });
      }
      await scheduleApi.confirm({
        type,
        number: String(form.get("number") ?? "").trim() || null,
        issuedAt: null,
        expiresAt: String(form.get("expiresAt") ?? "") || null,
        categories: null,
        files: confirmed,
      });
      setFront(undefined);
      setBack(undefined);
      await onSaved();
    } catch (failure) {
      setError(
        failure instanceof DriverRequestError && failure.code === "storage_unavailable"
          ? "Uploads are temporarily unavailable. Please try again later or call the office."
          : "Your document was not sent. Check your connection and try again.",
      );
    } finally {
      setBusy(false);
      setProgress("");
    }
  };
  return (
    <Drawer
      open
      title="Upload document"
      closeLabel="Close upload"
      onClose={() => {
        if (!busy) onClose();
      }}
    >
      <form className="driver-sheet" onSubmit={submit}>
        <label>
          Document type
          <select
            disabled={busy}
            value={type}
            onChange={(event) => {
              const next = event.target.value as DocumentType;
              setType(next);
              setTwoSides(["licence_c_ce", "driver_card", "id_card"].includes(next));
            }}
          >
            {documentTypes.map((item) => (
              <option key={item.value} value={item.value}>
                {item.label}
              </option>
            ))}
          </select>
        </label>
        {waiting.has(type) ? (
          <p className="driver-callout">
            This document is already awaiting verification. HR will review it before another upload.
          </p>
        ) : (
          <>
            <p>Keep all corners visible. Crop only empty space around the document.</p>
            <PhotoInput side="front" photo={front} onPhoto={setFront} />
            {twoSides ? (
              <PhotoInput side="back" photo={back} onPhoto={setBack} />
            ) : (
              <button type="button" disabled={busy} onClick={() => setTwoSides(true)}>
                Add second side
              </button>
            )}
            <label>
              Document number (optional)
              <input name="number" autoComplete="off" maxLength={100} disabled={busy} />
            </label>
            <label>
              Expiry date
              <input name="expiresAt" type="date" disabled={busy} />
            </label>
            <button
              type="submit"
              className="primary"
              disabled={busy || !online || !front || (twoSides && !back)}
            >
              {busy ? "Sending…" : "Send to HR"}
            </button>
          </>
        )}
        {progress && <p role="status">{progress}</p>}
        {error && (
          <p className="driver-error" role="alert">
            {error}
          </p>
        )}
      </form>
    </Drawer>
  );
}
