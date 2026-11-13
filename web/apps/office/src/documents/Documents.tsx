import { useEffect, useMemo, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { useInfiniteQuery, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, FileText, FileUp, Search, XCircle } from "lucide-react";
import { Banner, Drawer, EmptyState, Panel, Toast } from "@fleetops/ui";
import type { components } from "@fleetops/api-client";
import { useSession } from "../auth/AuthGate";
import { AuthRequestError } from "../auth/api";
import { en } from "../i18n/en";
import { formatDocumentDate } from "../shell/date";
import { fleetApi } from "../fleet/api";
import {
  documentTypes,
  documentsApi,
  typesWithExpiry,
  type DocumentOwnerType,
  type DocumentType,
} from "./api";
import { DocumentDialog, DocumentStatusPill, type DocumentDialogMode } from "./DocumentDialog";
import "./documents.css";

const accept = "image/jpeg,image/png,image/webp,image/heic,application/pdf";
const maxFileSize = 15 * 1024 * 1024;

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
  const initialParams = new URLSearchParams(window.location.search);
  const initialUpload = initialParams.get("upload")?.split(":") ?? [];
  const [uploadRequest, setUploadRequest] = useState({
    ownerId: initialUpload[1] ?? initialParams.get("ownerId") ?? "",
    ownerType: (initialUpload[0] === "employee" || initialUpload[0] === "vehicle"
      ? initialUpload[0]
      : undefined) as DocumentOwnerType | undefined,
    ownerName: initialParams.get("ownerName") ?? "",
    type: initialUpload[2] as DocumentType | undefined,
  });
  const [filter, setFilter] = useState(() =>
    ["queue", "all", "expiring", "expired", "missing"].includes(initialParams.get("filter") ?? "")
      ? (initialParams.get("filter") as string)
      : "queue",
  );
  const [dialogOpen, setDialogOpen] = useState(false);
  const [opened, setOpened] = useState<{ id: string; mode: DocumentDialogMode } | null>(() => {
    const id = initialParams.get("open");
    return id ? { id, mode: "view" } : null;
  });
  const [toast, setToast] = useState<string | null>(null);
  const roles = session.data?.roles ?? [];
  const canUpload = Boolean(roleOwner(roles));
  const canUploadFor = (ownerType: DocumentOwnerType) =>
    ownerType === "employee" ? roles.includes("hr") : roles.includes("dispatcher");
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    if ((params.has("upload") || params.has("ownerId")) && canUpload) setDialogOpen(true);
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
    queryFn: async ({ pageParam }) => {
      if (filter !== "missing")
        return documentsApi.list({ cursor: pageParam || undefined, search: term, filter });
      const result = await documentsApi.missing(pageParam || undefined);
      return {
        nextCursor: result.nextCursor,
        items: result.items.map((item) => ({
          id: item.ownerId,
          ownerType: item.ownerType,
          ownerId: item.ownerId,
          ownerName: item.ownerName,
          type: item.documentType,
          number: null,
          issuedAt: null,
          expiresAt: null,
          status: "rejected" as const,
          uploadedAt: "",
          version: 0,
          canAct: false,
        })),
      };
    },
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
      aria-pressed={filter === value}
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
          <div className="documents-toolbar">
            <div className="document-filters">
              {filterLabel("queue", en.documents.queue, counts.data?.queue)}
              {filterLabel("all", en.documents.all)}
              {filterLabel("expiring", en.documents.expiring, counts.data?.expiring)}
              {filterLabel("expired", en.documents.expired, counts.data?.expired)}
              {filterLabel("missing", en.documents.missing, counts.data?.missing)}
            </div>
            <label className="search-field">
              <Search size={16} aria-hidden />
              <span className="visually-hidden">{en.documents.searchLabel}</span>
              <input
                id="documents-search"
                placeholder={en.documents.searchPlaceholder}
                value={search}
                onChange={(event) => setSearch(event.target.value)}
              />
            </label>
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
                    roles={roles}
                    onChanged={refresh}
                    onReview={() => setOpened({ id: document.id, mode: "view" })}
                  />
                ))
              : null}
            {filter !== "queue" &&
              (filter === "missing" ? (
                <table>
                  <caption className="visually-hidden">{en.documents.tableCaption}</caption>
                  <thead>
                    <tr>
                      <th scope="col">{en.documents.ownerColumn}</th>
                      <th>{en.documents.typeColumn}</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {documents.map((document) => (
                      <tr key={`${document.ownerId}-${document.type}`}>
                        <td>{document.ownerName}</td>
                        <td>{en.documents.types[document.type]}</td>
                        <td>
                          {canUpload &&
                            ((document.ownerType === "employee" && session.data?.roles.includes("hr")) ||
                              (document.ownerType === "vehicle" &&
                                session.data?.roles.includes("dispatcher"))) && (
                              <button
                                className="ui-button primary"
                                type="button"
                                onClick={() => {
                                  setUploadRequest({
                                    ownerId: document.ownerId,
                                    ownerType: document.ownerType,
                                    ownerName: document.ownerName,
                                    type: document.type,
                                  });
                                  setDialogOpen(true);
                                }}
                              >
                                {en.documents.upload}
                              </button>
                            )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              ) : (
                <table className="documents-table">
                  <caption className="visually-hidden">{en.documents.tableCaption}</caption>
                  <thead>
                    <tr>
                      <th scope="col">{en.documents.documentColumn}</th>
                      <th scope="col">{en.documents.ownerColumn}</th>
                      <th scope="col">{en.documents.expiryColumn}</th>
                      <th scope="col">{en.documents.statusColumn}</th>
                      <th scope="col">{en.documents.actionColumn}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {documents.map((document) => {
                      const typeLabel = en.documents.types[document.type];
                      const name = `${typeLabel}, ${document.ownerName}`;
                      const awaiting = document.status === "uploaded" && document.canAct;
                      return (
                        <tr key={document.id}>
                          <td>
                            <strong className="document-name">{typeLabel}</strong>
                            <span className="document-sub">
                              {document.number ??
                                en.documents.uploadedOn.replace(
                                  "{date}",
                                  formatDocumentDate(document.uploadedAt),
                                )}
                            </span>
                          </td>
                          <td>{document.ownerName}</td>
                          <td className="tabular-nums">{formatDocumentDate(document.expiresAt)}</td>
                          <td>
                            <DocumentStatusPill
                              status={document.status}
                              expiresAt={document.expiresAt}
                              expiring={filter === "expiring"}
                            />
                          </td>
                          <td>
                            <div className="document-actions">
                              <button
                                className="ui-button"
                                type="button"
                                aria-label={`${en.documents.view}: ${name}`}
                                onClick={() => setOpened({ id: document.id, mode: "view" })}
                              >
                                {en.documents.view}
                              </button>
                              {awaiting ? (
                                <>
                                  <button
                                    className="ui-button primary"
                                    type="button"
                                    aria-label={`${en.documents.verify}: ${name}`}
                                    onClick={() => setOpened({ id: document.id, mode: "verify" })}
                                  >
                                    {en.documents.verify}
                                  </button>
                                  <button
                                    className="ui-button"
                                    type="button"
                                    aria-label={`${en.documents.reject}: ${name}`}
                                    onClick={() => setOpened({ id: document.id, mode: "reject" })}
                                  >
                                    {en.documents.reject}
                                  </button>
                                </>
                              ) : (
                                document.status !== "uploaded" &&
                                canUploadFor(document.ownerType) && (
                                  <button
                                    className="ui-button"
                                    type="button"
                                    aria-label={`${en.documents.uploadRenewal}: ${name}`}
                                    onClick={() => {
                                      setUploadRequest({
                                        ownerId: document.ownerId,
                                        ownerType: document.ownerType,
                                        ownerName: document.ownerName,
                                        type: document.type,
                                      });
                                      setDialogOpen(true);
                                    }}
                                  >
                                    {en.documents.uploadRenewal}
                                  </button>
                                )
                              )}
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              ))}
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
        key={`${uploadRequest.ownerType ?? ""}:${uploadRequest.ownerId}:${uploadRequest.type ?? ""}:${uploadRequest.ownerName}`}
        open={dialogOpen}
        roles={roles}
        initialOwnerId={uploadRequest.ownerId}
        initialOwnerType={uploadRequest.ownerType}
        initialOwnerName={uploadRequest.ownerName}
        initialType={uploadRequest.type}
        onClose={() => setDialogOpen(false)}
        onSaved={() => {
          setDialogOpen(false);
          setToast(en.documents.success);
          refresh();
        }}
      />
      {opened && (
        <DocumentDialog
          key={`${opened.id}:${opened.mode}`}
          id={opened.id}
          mode={opened.mode}
          roles={roles}
          onClose={() => setOpened(null)}
          onChanged={refresh}
          onDone={(message) => {
            setOpened(null);
            setToast(message);
            refresh();
          }}
        />
      )}
      <Toast
        open={toast !== null}
        onClose={() => setToast(null)}
        dismissLabel={en.documents.dismiss}
        undoLabel={en.documents.undo}
      >
        {toast}
      </Toast>
    </div>
  );
}

function QueueCard({
  document,
  roles,
  onChanged,
  onReview,
}: {
  document: components["schemas"]["DocumentListItem"];
  roles: string[];
  onChanged: () => void;
  onReview: () => void;
}) {
  const [reason, setReason] = useState("");
  const [deleteReason, setDeleteReason] = useState("");
  const [failure, setFailure] = useState(false);
  const [expiresAt, setExpiresAt] = useState("");
  const [number, setNumber] = useState(document.number ?? "");
  const [busy, setBusy] = useState(false);
  const canDelete = roles.includes("hr");
  const typeNeedsExpiry = typesWithExpiry.includes(document.type);
  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setFailure(false);
    try {
      await action();
      onChanged();
    } catch {
      setFailure(true);
    } finally {
      setBusy(false);
    }
  }
  return (
    <article className="queue-card" aria-busy={busy}>
      <button
        className="queue-thumb"
        type="button"
        onClick={onReview}
        aria-label={`${en.documents.reviewFile}: ${document.ownerName}`}
      >
        <FileText size={28} aria-hidden />
        <span>{en.documents.reviewFile}</span>
      </button>
      <div className="queue-card-body">
        <h3>{en.documents.types[document.type]}</h3>
        <span>{document.ownerName}</span>
        <small>
          {en.documents.uploadedColumn}{" "}
          <time dateTime={document.uploadedAt}>{formatDocumentDate(document.uploadedAt)}</time>
        </small>
        <p>{en.documents.reviewHint}</p>
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
          <h3>{en.documents.verificationDetails}</h3>
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
          <label className="queue-reason">
            {en.documents.rejectReason}
            <input value={reason} onChange={(event) => setReason(event.target.value)} />
          </label>
          <p className="queue-reason-hint">{en.documents.rejectHint}</p>
          <div className="queue-buttons">
            <button className="ui-button primary" disabled={busy} type="submit">
              <CheckCircle2 size={16} aria-hidden />
              {busy ? en.documents.saving : en.documents.verify}
            </button>
            <button
              className="ui-button ghost"
              disabled={busy || !reason.trim()}
              type="button"
              onClick={() =>
                void run(() => documentsApi.reject(document.id, { version: document.version, reason }))
              }
            >
              <XCircle size={16} aria-hidden />
              {en.documents.reject}
            </button>
          </div>
        </form>
      ) : (
        <p className="ui-muted">
          {document.ownerType === "vehicle" ? en.documents.dispatchReadOnly : en.documents.readOnly}
        </p>
      )}
      {canDelete && (
        <div className="queue-delete">
          <p>{en.documents.deleteHint}</p>
          <input
            aria-label={en.documents.deleteReason}
            placeholder={en.documents.deleteReason}
            value={deleteReason}
            onChange={(event) => setDeleteReason(event.target.value)}
          />
          <button
            className="ui-button danger"
            disabled={busy || !deleteReason.trim()}
            type="button"
            onClick={() =>
              void run(() =>
                documentsApi.remove(document.id, { version: document.version, reason: deleteReason.trim() }),
              )
            }
          >
            {en.documents.deleteMisupload}
          </button>
        </div>
      )}
      {failure && (
        <div className="queue-error">
          <Banner kind="error" label={en.documents.errorLabel}>
            {en.documents.actionError}
          </Banner>
        </div>
      )}
    </article>
  );
}

type UploadFile = { file: File; page: components["schemas"]["DocumentFilePage"] };

function UploadDialog({
  open,
  roles,
  initialOwnerId,
  initialOwnerType,
  initialOwnerName,
  initialType,
  onClose,
  onSaved,
}: {
  open: boolean;
  roles: string[];
  initialOwnerId: string;
  initialOwnerType?: DocumentOwnerType;
  initialOwnerName: string;
  initialType?: DocumentType;
  onClose: () => void;
  onSaved: () => void;
}) {
  const initialOwner = initialOwnerType ?? roleOwner(roles) ?? "employee";
  const [ownerType, setOwnerType] = useState<DocumentOwnerType>(initialOwner);
  const [ownerId, setOwnerId] = useState(initialOwnerId);
  const [ownerName, setOwnerName] = useState(initialOwnerName);
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
  const ownerNameQuery = useQuery({
    queryKey: ["document-owner", ownerType, ownerId],
    queryFn: async () => {
      if (ownerType === "vehicle") {
        const vehicle = await fleetApi.detail(ownerId);
        return `${vehicle.plate} · ${vehicle.make} ${vehicle.model}`;
      }
      const result = await documentsApi.people("", ownerId);
      return result.items.find((person) => person.id === ownerId)?.name ?? "";
    },
    enabled: open && Boolean(ownerId) && !ownerName,
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
    if (ownerNameQuery.data) setOwnerName(ownerNameQuery.data);
  }, [ownerNameQuery.data]);

  useEffect(() => {
    if (!availableTypes.includes(type)) setType(availableTypes[0]);
  }, [availableTypes, type]);
  const changeOwnerType = (nextOwnerType: DocumentOwnerType) => {
    if (nextOwnerType === ownerType) return;
    setOwnerType(nextOwnerType);
    setOwnerId("");
    setOwnerName("");
    setVehicleKind(null);
    setOwnerSearch("");
    setFiles([]);
  };
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
                onClick={() => changeOwnerType("employee")}
              >
                {en.documents.person}
              </button>
              <button
                type="button"
                className={ownerType === "vehicle" ? "selected" : ""}
                onClick={() => changeOwnerType("vehicle")}
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
