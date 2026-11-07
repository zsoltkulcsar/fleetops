import { useEffect, useMemo, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { useInfiniteQuery, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Clock3, FileUp, RefreshCw, Search, XCircle } from "lucide-react";
import { Banner, Drawer, EmptyState, Panel, Toast } from "@fleetops/ui";
import type { components } from "@fleetops/api-client";
import { useSession } from "../auth/AuthGate";
import { AuthRequestError } from "../auth/api";
import { en } from "../i18n/en";
import { formatDocumentDate } from "../shell/date";
import {
  documentTypes,
  documentsApi,
  type DocumentOwnerType,
  type DocumentStatus,
  type DocumentType,
} from "./api";
import "./documents.css";

const accept = "image/jpeg,image/png,image/webp,image/heic,application/pdf";
const maxFileSize = 15 * 1024 * 1024;
const statusIcons: Record<DocumentStatus, typeof CheckCircle2> = {
  uploaded: Clock3,
  verified: CheckCircle2,
  rejected: XCircle,
  replaced: RefreshCw,
};

function roleOwner(roles: string[]): DocumentOwnerType | null {
  const hr = roles.includes("hr");
  const dispatcher = roles.includes("dispatcher");
  if (hr && dispatcher) return "employee";
  if (hr) return "employee";
  if (dispatcher) return "vehicle";
  return null;
}

export function Documents() {
  const heading = useRef<HTMLHeadingElement>(null);
  const session = useSession();
  const client = useQueryClient();
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const uploadTarget = new URLSearchParams(window.location.search).get("upload")?.split(":") ?? [];
  const [filter, setFilter] = useState(() =>
    new URLSearchParams(window.location.search).get("filter") === "queue" ? "queue" : "queue",
  );
  const [dialogOpen, setDialogOpen] = useState(false);
  const [openId, setOpenId] = useState<string | null>(
    new URLSearchParams(window.location.search).get("open"),
  );
  const [toast, setToast] = useState(false);
  const canUpload = Boolean(roleOwner(session.data?.roles ?? []));
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    if ((params.get("filter") === "missing" || params.has("upload")) && canUpload) setDialogOpen(true);
  }, [canUpload]);

  useEffect(() => {
    document.title = `Documents · ${en.appName}`;
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => setTerm(search.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [search]);

  const query = useInfiniteQuery({
    queryKey: ["documents", term, filter],
    initialPageParam: "",
    queryFn: ({ pageParam }) => documentsApi.list({ cursor: pageParam || undefined, search: term, filter }),
    getNextPageParam: (page) => page.nextCursor ?? undefined,
  });
  const documents = useMemo(() => query.data?.pages.flatMap((page) => page.items) ?? [], [query.data]);
  const counts = useQuery({
    queryKey: ["document-counts"],
    queryFn: documentsApi.counts,
    refetchInterval: 60_000,
  });
  useEffect(() => {
    if (query.isSuccess && documents.length === 0 && counts.data?.queue === 0 && filter === "queue") {
      setFilter("all");
    }
  }, [counts.data?.queue, documents.length, filter, query.isSuccess]);
  useEffect(() => {
    const open = () => document.getElementById("documents-search")?.focus();
    window.addEventListener("fleetops:focus-search", open);
    return () => window.removeEventListener("fleetops:focus-search", open);
  }, []);
  const refresh = () => {
    void client.invalidateQueries({ queryKey: ["documents"] });
    void client.invalidateQueries({ queryKey: ["document-counts"] });
  };
  const filterLabel = (value: string, label: string, count?: number) => (
    <button
      type="button"
      className={filter === value ? "document-filter selected" : "document-filter"}
      onClick={() => setFilter(value)}
    >
      {label}
      {count === undefined ? "" : ` ${count}`}
    </button>
  );

  return (
    <div className="page documents-page">
      <div className="page-title">
        <div>
          <span className="eyebrow">{en.documents.eyebrow}</span>
          <h1 ref={heading} tabIndex={-1}>
            {en.documents.title}
          </h1>
          <p className="ui-muted">{en.documents.subtitle}</p>
        </div>
        {canUpload && (
          <button className="ui-button primary" type="button" onClick={() => setDialogOpen(true)}>
            <FileUp size={17} aria-hidden /> {en.documents.upload}
          </button>
        )}
      </div>
      {query.isError && (
        <Banner
          label={en.documents.errorLabel}
          retryLabel={en.documents.retry}
          onRetry={() => void query.refetch()}
        >
          {en.documents.error}
        </Banner>
      )}
      <Panel
        title={filter === "queue" ? en.documents.queue : en.documents.panelTitle}
        filters={
          <div className="document-filters">
            {filterLabel("queue", en.documents.queue, counts.data?.queue)}
            {filterLabel("all", en.documents.all)}
            {filterLabel("expiring", en.documents.expiring, counts.data?.expiring)}
            {filterLabel("expired", en.documents.expired, counts.data?.expired)}
          </div>
        }
      >
        {query.isLoading ? (
          <div className="documents-loading">{en.documents.loading}</div>
        ) : !documents.length && !query.isError ? (
          <EmptyState
            heading={
              filter === "queue"
                ? en.documents.nothingToVerify
                : term
                  ? en.documents.noResults
                  : en.documents.empty
            }
          >
            {!term && filter !== "queue" && en.documents.emptyBody}
          </EmptyState>
        ) : (
          <div
            className={filter === "queue" ? "document-queue" : "table-wrap"}
            tabIndex={0}
            role="region"
            aria-label="Documents table"
          >
            {filter === "queue"
              ? documents.map((document) => (
                  <QueueCard
                    key={document.id}
                    document={document}
                    roles={session.data?.roles ?? []}
                    onChanged={refresh}
                  />
                ))
              : null}
            {filter !== "queue" && (
              <table>
                <caption className="visually-hidden">{en.documents.tableCaption}</caption>
                <thead>
                  <tr>
                    <th>{en.documents.typeColumn}</th>
                    <th>{en.documents.ownerColumn}</th>
                    <th>{en.documents.numberColumn}</th>
                    <th>{en.documents.issuedColumn}</th>
                    <th>{en.documents.expiresColumn}</th>
                    <th>{en.documents.statusColumn}</th>
                    <th>{en.documents.uploadedColumn}</th>
                  </tr>
                </thead>
                <tbody>
                  {documents.map((document) => {
                    const Icon = statusIcons[document.status];
                    return (
                      <tr key={document.id} onClick={() => setOpenId(document.id)} className="document-row">
                        <td>{en.documents.types[document.type]}</td>
                        <td>{document.ownerName}</td>
                        <td>{document.number ?? "—"}</td>
                        <td className="tabular-nums">{formatDocumentDate(document.issuedAt)}</td>
                        <td className="tabular-nums">{formatDocumentDate(document.expiresAt)}</td>
                        <td>
                          <span className={`document-status status-${document.status}`}>
                            <Icon size={15} aria-hidden /> {en.documents.status[document.status]}
                          </span>
                        </td>
                        <td className="tabular-nums">{formatDocumentDate(document.uploadedAt)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
          </div>
        )}
        {query.hasNextPage && (
          <button
            className="ui-button load-more"
            type="button"
            onClick={() => void query.fetchNextPage()}
            disabled={query.isFetchingNextPage}
          >
            {query.isFetchingNextPage ? "Loading…" : "Load more"}
          </button>
        )}
      </Panel>
      <UploadDialog
        open={dialogOpen}
        roles={session.data?.roles ?? []}
        initialOwnerId={uploadTarget[1] ?? new URLSearchParams(window.location.search).get("ownerId") ?? ""}
        initialOwnerType={
          uploadTarget[0] === "employee" || uploadTarget[0] === "vehicle"
            ? uploadTarget[0]
            : new URLSearchParams(window.location.search).get("ownerId")
              ? "vehicle"
              : undefined
        }
        initialType={uploadTarget[2] as DocumentType | undefined}
        onClose={() => setDialogOpen(false)}
        onSaved={() => {
          setDialogOpen(false);
          setToast(true);
          refresh();
        }}
      />
      {openId && (
        <DocumentDetailDrawer
          id={openId}
          roles={session.data?.roles ?? []}
          onClose={() => setOpenId(null)}
          onChanged={refresh}
        />
      )}
      <Toast
        open={toast}
        onClose={() => setToast(false)}
        dismissLabel={en.documents.dismiss}
        undoLabel={en.documents.undo}
      >
        {en.documents.success}
      </Toast>
    </div>
  );
}

function QueueCard({
  document,
  roles,
  onChanged,
}: {
  document: components["schemas"]["DocumentListItem"];
  roles: string[];
  onChanged: () => void;
}) {
  const [reason, setReason] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const [number, setNumber] = useState(document.number ?? "");
  const [busy, setBusy] = useState(false);
  const canDelete = roles.includes("hr");
  const typeNeedsExpiry = [
    "licence_c_ce",
    "driver_card",
    "code95",
    "medical",
    "adr_certificate",
    "id_card",
    "hu_report",
    "sp_report",
    "tacho_calibration",
    "insurance",
    "community_licence",
  ].includes(document.type);
  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    try {
      await action();
      onChanged();
    } finally {
      setBusy(false);
    }
  }
  return (
    <article className="queue-card">
      <div className="queue-thumb">
        <FileUp size={22} aria-hidden />
        <span>{en.documents.types[document.type]}</span>
      </div>
      <div className="queue-card-body">
        <strong>{en.documents.types[document.type]}</strong>
        <span>{document.ownerName}</span>
        <small>{formatDocumentDate(document.uploadedAt)}</small>
      </div>
      {document.canAct ? (
        <form
          className="queue-actions"
          onSubmit={(event) => {
            event.preventDefault();
            void run(() =>
              documentsApi.verify(document.id, {
                version: document.version,
                expiresAt: expiresAt || null,
                issuedAt: null,
                number: number.trim() || null,
                categories: null,
              }),
            );
          }}
        >
          {typeNeedsExpiry && (
            <label>
              {en.documents.expires}
              <input
                type="date"
                required
                value={expiresAt}
                onChange={(event) => setExpiresAt(event.target.value)}
              />
            </label>
          )}
          <label>
            {en.documents.number}
            <input value={number} onChange={(event) => setNumber(event.target.value)} />
          </label>
          <div className="queue-buttons">
            <button className="ui-button primary" disabled={busy} type="submit">
              {en.documents.verify}
            </button>
            <button
              className="ui-button ghost"
              disabled={busy || !reason.trim()}
              type="button"
              onClick={() =>
                void run(() => documentsApi.reject(document.id, { version: document.version, reason }))
              }
            >
              {en.documents.reject}
            </button>
          </div>
          <label>
            {en.documents.rejectReason}
            <input value={reason} onChange={(event) => setReason(event.target.value)} />
          </label>
        </form>
      ) : (
        <p className="ui-muted">
          {document.ownerType === "vehicle" ? en.documents.dispatchReadOnly : en.documents.readOnly}
        </p>
      )}
      {canDelete && (
        <div className="queue-delete">
          <input
            aria-label={en.documents.deleteReason}
            placeholder={en.documents.deleteReason}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
          />
          <button
            className="ui-button danger"
            disabled={busy || !reason.trim()}
            type="button"
            onClick={() =>
              void run(() => documentsApi.remove(document.id, { version: document.version, reason }))
            }
          >
            {en.documents.deleteMisupload}
          </button>
        </div>
      )}
    </article>
  );
}

function DocumentDetailDrawer({
  id,
  roles,
  onClose,
  onChanged,
}: {
  id: string;
  roles: string[];
  onClose: () => void;
  onChanged: () => void;
}) {
  const detail = useQuery({ queryKey: ["document", id], queryFn: () => documentsApi.detail(id) });
  const [checkedAt, setCheckedAt] = useState(new Date().toISOString().slice(0, 10));
  if (!detail.data)
    return (
      <Drawer open title={en.documents.detail} onClose={onClose} closeLabel={en.documents.close}>
        {en.documents.loading}
      </Drawer>
    );
  const item = detail.data;
  return (
    <Drawer open title={item.ownerName} onClose={onClose} closeLabel={en.documents.close}>
      <div className="document-detail">
        <p>
          <strong>{en.documents.types[item.type]}</strong>
          <br />
          {en.documents.status[item.status]}
        </p>
        {item.rejectReason && <p className="document-reject-reason">{item.rejectReason}</p>}
        <ul>
          {item.files.map((file) => (
            <li key={file.id}>
              <button
                type="button"
                className="document-link"
                onClick={async () => {
                  const link = await documentsApi.fileLink(item.id, file.id);
                  window.open(link.url, "_blank", "noopener,noreferrer");
                }}
              >
                {file.contentType === "application/pdf" ? "Open PDF" : `Open ${file.page}`}
              </button>
            </li>
          ))}
        </ul>
        {item.type === "licence_c_ce" && item.status === "verified" && (
          <form
            onSubmit={(event) => {
              event.preventDefault();
              void documentsApi.sightCheck(item.id, { version: item.version, checkedAt }).then(onChanged);
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
    </Drawer>
  );
}

type UploadFile = { file: File; page: components["schemas"]["DocumentFilePage"] };

function UploadDialog({
  open,
  roles,
  initialOwnerId,
  initialOwnerType,
  initialType,
  onClose,
  onSaved,
}: {
  open: boolean;
  roles: string[];
  initialOwnerId: string;
  initialOwnerType?: DocumentOwnerType;
  initialType?: DocumentType;
  onClose: () => void;
  onSaved: () => void;
}) {
  const initialOwner = initialOwnerType ?? roleOwner(roles) ?? "employee";
  const [ownerType, setOwnerType] = useState<DocumentOwnerType>(initialOwner);
  const [ownerId, setOwnerId] = useState(initialOwnerId);
  const [ownerName, setOwnerName] = useState("");
  const [vehicleKind, setVehicleKind] = useState<components["schemas"]["VehicleKind"] | null>(null);
  const [ownerSearch, setOwnerSearch] = useState("");
  const [type, setType] = useState<DocumentType>(
    initialType && documentTypes[initialOwner].includes(initialType)
      ? initialType
      : documentTypes[initialOwner][0],
  );
  const [files, setFiles] = useState<UploadFile[]>([]);
  const [number, setNumber] = useState("");
  const [issuedAt, setIssuedAt] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const [categories, setCategories] = useState("");
  const [failure, setFailure] = useState<string | null>(null);
  const [progress, setProgress] = useState("");
  const [saving, setSaving] = useState(false);
  const bothRoles = roles.includes("hr") && roles.includes("dispatcher");
  const peopleQuery = useQuery({
    queryKey: ["people-lookup", ownerSearch],
    queryFn: () => documentsApi.people(ownerSearch),
    enabled: ownerType === "employee" && ownerSearch.trim().length > 1,
  });
  const vehiclesQuery = useQuery({
    queryKey: ["vehicles-lookup", ownerSearch],
    queryFn: () => documentsApi.vehicles(ownerSearch),
    enabled: ownerType === "vehicle" && ownerSearch.trim().length > 1,
  });
  const availableTypes = documentTypes[ownerType].filter(
    (item) =>
      !(
        ownerType === "vehicle" &&
        vehicleKind === "trailer" &&
        (item === "tacho_calibration" || item === "community_licence")
      ),
  );
  const needsPages = type === "licence_c_ce" || type === "driver_card";

  useEffect(() => {
    if (!availableTypes.includes(type)) setType(availableTypes[0]);
  }, [availableTypes, type]);
  useEffect(() => {
    setOwnerId("");
    setOwnerName("");
    setVehicleKind(null);
    setOwnerSearch("");
    setFiles([]);
  }, [ownerType]);
  const selectFiles = (
    event: ChangeEvent<HTMLInputElement>,
    page: components["schemas"]["DocumentFilePage"],
  ) => {
    const selected = [...(event.target.files ?? [])];
    if (!needsPages && selected.length > 10) {
      setFailure(en.documents.tooManyFiles);
      event.target.value = "";
      return;
    }
    const invalid = selected.find(
      (file) => !accept.split(",").includes(file.type) || file.size > maxFileSize,
    );
    if (invalid) {
      setFailure(en.documents.fileRejected);
      event.target.value = "";
      return;
    }
    setFailure(null);
    if (needsPages)
      setFiles((current) => [
        ...current.filter((item) => item.page !== page),
        ...selected.map((file) => ({ file, page })),
      ]);
    else setFiles(selected.map((file) => ({ file, page: "page" })));
  };
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setFailure(null);
    if (
      !ownerId ||
      !files.length ||
      (needsPages &&
        (files.every((item) => item.page !== "front") || files.every((item) => item.page !== "back")))
    ) {
      setFailure(en.documents.formRequired);
      return;
    }
    setSaving(true);
    try {
      const confirmedFiles: components["schemas"]["ConfirmFileInput"][] = [];
      for (const [index, item] of files.entries()) {
        setProgress(
          en.documents.progress
            .replace("{current}", String(index + 1))
            .replace("{total}", String(files.length)),
        );
        const link = await documentsApi.uploadLink({
          ownerType,
          ownerId,
          type,
          contentType: item.file.type,
          sizeBytes: item.file.size,
        });
        const upload = await fetch(link.url.url, {
          method: "PUT",
          headers: link.requiredHeaders,
          body: item.file,
          credentials: "omit",
        });
        if (!upload.ok) throw new AuthRequestError(upload.status, {});
        confirmedFiles.push({ fileId: link.fileId, page: item.page });
      }
      await documentsApi.create({
        ownerType,
        ownerId,
        type,
        number: number.trim() || null,
        issuedAt: issuedAt || null,
        expiresAt: expiresAt || null,
        categories:
          type === "licence_c_ce"
            ? categories
                .split(",")
                .map((item) => item.trim())
                .filter(Boolean)
            : null,
        files: confirmedFiles,
      });
      onSaved();
    } catch (error) {
      if (error instanceof AuthRequestError && error.problem.code === "storage_unavailable")
        setFailure(en.documents.storageUnavailable);
      else if (error instanceof AuthRequestError && error.problem.code === "file_rejected")
        setFailure(en.documents.fileRejected);
      else setFailure(en.documents.uploadFailed);
    } finally {
      setSaving(false);
      setProgress("");
    }
  };
  return (
    <Drawer open={open} title={en.documents.uploadTitle} onClose={onClose} closeLabel={en.documents.close}>
      {failure && <Banner label={en.documents.uploadError}>{failure}</Banner>}
      <form className="document-form" onSubmit={submit}>
        {bothRoles && (
          <fieldset>
            <legend>{en.documents.ownerType}</legend>
            <div className="document-switch">
              <button
                type="button"
                className={ownerType === "employee" ? "selected" : ""}
                onClick={() => setOwnerType("employee")}
              >
                {en.documents.person}
              </button>
              <button
                type="button"
                className={ownerType === "vehicle" ? "selected" : ""}
                onClick={() => setOwnerType("vehicle")}
              >
                {en.documents.vehicle}
              </button>
            </div>
          </fieldset>
        )}
        <label>
          {en.documents.owner}
          <input
            value={ownerName || ownerSearch}
            placeholder={
              ownerType === "employee" ? en.documents.peoplePlaceholder : en.documents.vehiclesPlaceholder
            }
            onChange={(event) => {
              setOwnerName("");
              setOwnerSearch(event.target.value);
              setOwnerId("");
              setVehicleKind(null);
            }}
          />
        </label>
        {ownerSearch.length > 1 && !ownerId && (
          <div className="lookup-results">
            {ownerType === "employee"
              ? peopleQuery.data?.items.map((person) => (
                  <button
                    type="button"
                    key={person.id}
                    onClick={() => {
                      setOwnerId(person.id);
                      setOwnerName(person.name);
                      setOwnerSearch("");
                    }}
                  >
                    {person.name}
                  </button>
                ))
              : vehiclesQuery.data?.items
                  .filter((vehicle) => vehicle.status === "active")
                  .map((vehicle) => (
                    <button
                      type="button"
                      key={vehicle.id}
                      onClick={() => {
                        setOwnerId(vehicle.id);
                        setOwnerName(`${vehicle.plate} · ${vehicle.make} ${vehicle.model}`);
                        setOwnerSearch("");
                        setVehicleKind(vehicle.kind);
                      }}
                    >
                      {vehicle.plate} · {vehicle.make} {vehicle.model}
                    </button>
                  ))}
          </div>
        )}
        <label>
          {en.documents.type}
          <select
            value={type}
            onChange={(event) => {
              const nextType = availableTypes.find((item) => item === event.target.value);
              if (nextType) setType(nextType);
            }}
          >
            {availableTypes.map((item) => (
              <option key={item} value={item}>
                {en.documents.types[item]}
              </option>
            ))}
          </select>
        </label>
        <div className="document-file-fields">
          {needsPages ? (
            <>
              <label>
                {en.documents.front}
                <input
                  type="file"
                  accept={accept}
                  required
                  onChange={(event) => selectFiles(event, "front")}
                />
              </label>
              <label>
                {en.documents.back}
                <input
                  type="file"
                  accept={accept}
                  required
                  onChange={(event) => selectFiles(event, "back")}
                />
              </label>
            </>
          ) : (
            <label>
              {en.documents.files}
              <input
                type="file"
                accept={accept}
                multiple
                required
                onChange={(event) => selectFiles(event, "page")}
              />
            </label>
          )}
          <p className="ui-muted">{en.documents.fileHint}</p>
        </div>
        <label>
          {en.documents.number}
          <input value={number} onChange={(event) => setNumber(event.target.value)} />
        </label>
        <div className="document-date-fields">
          <label>
            {en.documents.issued}
            <input type="date" value={issuedAt} onChange={(event) => setIssuedAt(event.target.value)} />
          </label>
          <label>
            {en.documents.expires}
            <input type="date" value={expiresAt} onChange={(event) => setExpiresAt(event.target.value)} />
          </label>
        </div>
        <p className="document-hint">{en.documents.expiryHint}</p>
        {type === "licence_c_ce" && (
          <label>
            {en.documents.categories}
            <input value={categories} onChange={(event) => setCategories(event.target.value)} />
          </label>
        )}
        {progress && (
          <p role="status" className="document-progress">
            {progress}
          </p>
        )}
        <button className="ui-button primary" type="submit" disabled={saving}>
          {saving ? en.documents.uploading : en.documents.save}
        </button>
      </form>
    </Drawer>
  );
}
