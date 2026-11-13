import { useEffect, useState, type FormEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import { CheckCircle2, RefreshCw, XCircle } from "lucide-react";
import { Banner, Drawer, StatusPill } from "@fleetops/ui";
import type { components } from "@fleetops/api-client";
import { en } from "../i18n/en";
import { formatDocumentDate } from "../shell/date";
import { documentsApi, typesWithExpiry, type DocumentStatus } from "./api";

export type DocumentDialogMode = "view" | "verify" | "reject";
type DocumentFile = components["schemas"]["DocumentFileDetail"];

function berlinToday() {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Berlin" }).format(new Date());
}

/** One pill per row: the verification state, or Expired once a verified document has run out. */
export function DocumentStatusPill({
  status,
  expiresAt,
  expiring = false,
}: {
  status: DocumentStatus;
  expiresAt: string | null;
  expiring?: boolean;
}) {
  if (status === "uploaded") return <StatusPill status="uploaded" />;
  if (status === "rejected") return <StatusPill status="rejected" />;
  if (status === "replaced")
    return (
      <span className="ui-status status-idle" data-status="replaced">
        <RefreshCw size={14} aria-hidden />
        <span>{en.documents.status.replaced}</span>
      </span>
    );
  if (expiresAt && expiresAt < berlinToday())
    return <StatusPill status="expired" date={expiresAt} label={en.documents.expired} />;
  if (expiring && expiresAt)
    return <StatusPill status="expiring" date={expiresAt} label={en.documents.expiring} />;
  return <StatusPill status="verified" label={en.documents.status.verified} />;
}

type PreviewState =
  | { status: "loading" }
  | { status: "error"; source?: string }
  | { status: "ready"; kind: "pdf" | "image"; url: string };

/**
 * Shows a stored file inside the page. Images load straight from the signed link; a PDF is
 * fetched and framed from a blob URL, so the file never opens in another window.
 */
function DocumentPreview({
  documentId,
  file,
  label,
}: {
  documentId: string;
  file: DocumentFile;
  label: string;
}) {
  const [state, setState] = useState<PreviewState>({ status: "loading" });
  const [attempt, setAttempt] = useState(0);
  const isPdf = file.contentType === "application/pdf";
  useEffect(() => {
    let cancelled = false;
    let blobUrl: string | null = null;
    setState({ status: "loading" });
    void (async () => {
      let source: string | undefined;
      try {
        const link = await documentsApi.fileLink(documentId, file.id);
        source = link.url;
        if (!isPdf) {
          if (!cancelled) setState({ status: "ready", kind: "image", url: link.url });
          return;
        }
        const response = await fetch(link.url, { credentials: "omit" });
        if (!response.ok) throw new Error("preview_unavailable");
        const blob = new Blob([await response.arrayBuffer()], { type: "application/pdf" });
        if (cancelled) return;
        blobUrl = URL.createObjectURL(blob);
        setState({ status: "ready", kind: "pdf", url: blobUrl });
      } catch {
        if (!cancelled) setState({ status: "error", source });
      }
    })();
    return () => {
      cancelled = true;
      if (blobUrl) URL.revokeObjectURL(blobUrl);
    };
  }, [documentId, file.id, isPdf, attempt]);

  if (state.status === "loading")
    return (
      <div className="document-preview-empty" role="status">
        {en.documents.previewLoading}
      </div>
    );
  if (state.status === "error")
    return (
      <div className="document-preview-empty">
        <p role="alert">{en.documents.previewError}</p>
        <div className="document-preview-fallback">
          <button className="ui-button" type="button" onClick={() => setAttempt((value) => value + 1)}>
            {en.documents.retry}
          </button>
          {state.source && (
            <a className="ui-button ghost" href={state.source} target="_blank" rel="noopener noreferrer">
              {en.documents.openInNewTab}
            </a>
          )}
        </div>
      </div>
    );
  return state.kind === "pdf" ? (
    <iframe className="document-preview-frame" title={label} src={state.url} />
  ) : (
    <img
      className="document-preview-image"
      alt={label}
      src={state.url}
      onError={() => setState({ status: "error", source: state.url })}
    />
  );
}

function fileLabel(file: DocumentFile, index: number) {
  if (file.page === "front") return en.documents.front;
  if (file.page === "back") return en.documents.back;
  return en.documents.pageNumber.replace("{n}", String(index + 1));
}

export function DocumentDialog({
  id,
  mode: initialMode,
  roles,
  onClose,
  onChanged,
  onDone,
}: {
  id: string;
  mode: DocumentDialogMode;
  roles: string[];
  onClose: () => void;
  /** The document changed but the dialog stays open. */
  onChanged: () => void;
  /** Verified or rejected: the dialog closes and the page confirms with this message. */
  onDone: (message: string) => void;
}) {
  const detail = useQuery({ queryKey: ["document", id], queryFn: () => documentsApi.detail(id) });
  const [mode, setMode] = useState<DocumentDialogMode>(initialMode);
  const [fileIndex, setFileIndex] = useState(0);
  const [expiresAt, setExpiresAt] = useState<string | null>(null);
  const [number, setNumber] = useState<string | null>(null);
  const [reason, setReason] = useState("");
  const [checkedAt, setCheckedAt] = useState(new Date().toISOString().slice(0, 10));
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState(false);

  if (detail.isError)
    return (
      <Drawer
        open
        variant="modal"
        title={en.documents.detail}
        onClose={onClose}
        closeLabel={en.documents.close}
      >
        <Banner
          kind="error"
          label={en.documents.errorLabel}
          retryLabel={en.documents.retry}
          onRetry={() => void detail.refetch()}
        >
          {en.documents.error}
        </Banner>
      </Drawer>
    );
  if (!detail.data)
    return (
      <Drawer
        open
        variant="modal"
        title={en.documents.detail}
        onClose={onClose}
        closeLabel={en.documents.close}
      >
        {en.documents.loading}
      </Drawer>
    );

  const item = detail.data;
  const typeLabel = en.documents.types[item.type];
  const canReview = item.canAct && item.status === "uploaded";
  const activeMode: DocumentDialogMode = canReview ? mode : "view";
  const needsExpiry = typesWithExpiry.includes(item.type);
  const expiryValue = expiresAt ?? item.expiresAt ?? "";
  const numberValue = number ?? item.number ?? "";
  const file = item.files[Math.min(fileIndex, item.files.length - 1)];

  async function run(action: () => Promise<unknown>, message: string) {
    setBusy(true);
    setFailure(false);
    try {
      await action();
      onDone(message);
    } catch {
      setFailure(true);
    } finally {
      setBusy(false);
    }
  }
  const verify = (event: FormEvent) => {
    event.preventDefault();
    void run(
      () =>
        documentsApi.verify(item.id, {
          version: item.version,
          expiresAt: expiryValue || null,
          issuedAt: item.issuedAt,
          number: numberValue.trim() || null,
          categories: item.categories.length ? item.categories : null,
        }),
      en.documents.verifiedToast,
    );
  };
  const reject = (event: FormEvent) => {
    event.preventDefault();
    void run(
      () => documentsApi.reject(item.id, { version: item.version, reason: reason.trim() }),
      en.documents.rejectedToast,
    );
  };

  return (
    <Drawer
      open
      variant="modal"
      title={activeMode === "view" ? typeLabel : `${en.documents.verifyTitle}: ${typeLabel}`}
      onClose={onClose}
      closeLabel={en.documents.close}
    >
      <div className="document-dialog">
        <section className="document-preview" aria-label={en.documents.previewLabel}>
          {item.files.length > 1 && (
            <div className="document-preview-tabs">
              {item.files.map((entry, index) => (
                <button
                  key={entry.id}
                  type="button"
                  className={entry.id === file?.id ? "document-filter selected" : "document-filter"}
                  aria-pressed={entry.id === file?.id}
                  onClick={() => setFileIndex(index)}
                >
                  {fileLabel(entry, index)}
                </button>
              ))}
            </div>
          )}
          {file ? (
            <DocumentPreview
              key={file.id}
              documentId={item.id}
              file={file}
              label={`${typeLabel} · ${item.ownerName}`}
            />
          ) : (
            <div className="document-preview-empty">{en.documents.noFiles}</div>
          )}
        </section>
        <div className="document-dialog-side">
          <dl className="document-facts">
            <div>
              <dt>{en.documents.typeColumn}</dt>
              <dd>{typeLabel}</dd>
            </div>
            <div>
              <dt>{en.documents.ownerColumn}</dt>
              <dd>{item.ownerName}</dd>
            </div>
            <div>
              <dt>{en.documents.statusColumn}</dt>
              <dd>
                <DocumentStatusPill status={item.status} expiresAt={item.expiresAt} />
              </dd>
            </div>
            <div>
              <dt>{en.documents.numberColumn}</dt>
              <dd>{item.number ?? "—"}</dd>
            </div>
            <div>
              <dt>{en.documents.issuedColumn}</dt>
              <dd className="tabular-nums">{formatDocumentDate(item.issuedAt)}</dd>
            </div>
            <div>
              <dt>{en.documents.expiryColumn}</dt>
              <dd className="tabular-nums">{formatDocumentDate(item.expiresAt)}</dd>
            </div>
            <div>
              <dt>{en.documents.uploadedColumn}</dt>
              <dd className="tabular-nums">{formatDocumentDate(item.uploadedAt)}</dd>
            </div>
          </dl>
          {item.rejectReason && <p className="document-reject-reason">{item.rejectReason}</p>}
          {failure && (
            <Banner kind="error" label={en.documents.errorLabel}>
              {en.documents.actionError}
            </Banner>
          )}
          {activeMode === "verify" && (
            <form className="document-form document-review" onSubmit={verify} aria-busy={busy}>
              <h3>{en.documents.verificationDetails}</h3>
              <p className="ui-muted">{en.documents.reviewHint}</p>
              {needsExpiry && (
                <label>
                  {en.documents.expires}
                  <input
                    type="date"
                    required
                    value={expiryValue}
                    onChange={(event) => setExpiresAt(event.target.value)}
                  />
                </label>
              )}
              <label>
                {en.documents.number}
                <input value={numberValue} onChange={(event) => setNumber(event.target.value)} />
              </label>
              <div className="document-dialog-actions">
                <button className="ui-button primary" type="submit" disabled={busy}>
                  <CheckCircle2 size={16} aria-hidden />
                  {busy ? en.documents.saving : en.documents.verify}
                </button>
                <button className="ui-button" type="button" disabled={busy} onClick={() => setMode("reject")}>
                  {en.documents.rejectInstead}
                </button>
              </div>
            </form>
          )}
          {activeMode === "reject" && (
            <form className="document-form document-review" onSubmit={reject} aria-busy={busy}>
              <h3>{en.documents.rejectTitle}</h3>
              <label>
                {en.documents.rejectReason}
                <textarea
                  required
                  rows={3}
                  value={reason}
                  onChange={(event) => setReason(event.target.value)}
                />
              </label>
              <p className="ui-muted">{en.documents.rejectDialogHint}</p>
              <div className="document-dialog-actions">
                <button className="ui-button danger" type="submit" disabled={busy || !reason.trim()}>
                  <XCircle size={16} aria-hidden />
                  {busy ? en.documents.saving : en.documents.reject}
                </button>
                <button className="ui-button" type="button" disabled={busy} onClick={() => setMode("verify")}>
                  {en.documents.backToVerify}
                </button>
              </div>
            </form>
          )}
          {activeMode === "view" && canReview && (
            <div className="document-dialog-actions">
              <button className="ui-button primary" type="button" onClick={() => setMode("verify")}>
                {en.documents.verify}
              </button>
              <button className="ui-button" type="button" onClick={() => setMode("reject")}>
                {en.documents.reject}
              </button>
            </div>
          )}
          {activeMode === "view" && item.type === "licence_c_ce" && item.status === "verified" && (
            <form
              className="document-form"
              onSubmit={(event) => {
                event.preventDefault();
                void documentsApi
                  .sightCheck(item.id, { version: item.version, checkedAt })
                  .then(() => {
                    onChanged();
                    void detail.refetch();
                  })
                  .catch(() => setFailure(true));
              }}
            >
              <label>
                {en.documents.sightCheckDate}
                <input type="date" value={checkedAt} onChange={(event) => setCheckedAt(event.target.value)} />
              </label>
              <p className="ui-muted">{en.documents.sightCheckNote}</p>
              <button className="ui-button primary" type="submit">
                {en.documents.recordSightCheck}
              </button>
            </form>
          )}
          {item.canAct && roles.includes("hr") && <p className="ui-muted">{en.documents.deleteAvailable}</p>}
        </div>
      </div>
    </Drawer>
  );
}
