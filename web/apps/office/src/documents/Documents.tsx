import { useEffect, useMemo, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
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
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [dialogOpen, setDialogOpen] = useState(false);
  const [toast, setToast] = useState(false);
  const canUpload = Boolean(roleOwner(session.data?.roles ?? []));

  useEffect(() => {
    document.title = `Documents · ${en.appName}`;
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => setTerm(search.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [search]);

  const query = useInfiniteQuery({
    queryKey: ["documents", term],
    initialPageParam: "",
    queryFn: ({ pageParam }) => documentsApi.list({ cursor: pageParam || undefined, search: term }),
    getNextPageParam: (page) => page.nextCursor ?? undefined,
  });
  const documents = useMemo(() => query.data?.pages.flatMap((page) => page.items) ?? [], [query.data]);

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
        title={en.documents.panelTitle}
        filters={
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
        }
      >
        {query.isLoading ? (
          <div className="documents-loading">{en.documents.loading}</div>
        ) : !documents.length && !query.isError ? (
          <EmptyState heading={term ? en.documents.noResults : en.documents.empty}>
            {!term && en.documents.emptyBody}
          </EmptyState>
        ) : (
          <div className="table-wrap" tabIndex={0} role="region" aria-label="Documents table">
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
                    <tr key={document.id}>
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
        onClose={() => setDialogOpen(false)}
        onSaved={() => {
          setDialogOpen(false);
          setToast(true);
          void query.refetch();
        }}
      />
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

type UploadFile = { file: File; page: components["schemas"]["DocumentFilePage"] };

function UploadDialog({
  open,
  roles,
  onClose,
  onSaved,
}: {
  open: boolean;
  roles: string[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const initialOwner = roleOwner(roles) ?? "employee";
  const [ownerType, setOwnerType] = useState<DocumentOwnerType>(initialOwner);
  const [ownerId, setOwnerId] = useState("");
  const [ownerName, setOwnerName] = useState("");
  const [vehicleKind, setVehicleKind] = useState<components["schemas"]["VehicleKind"] | null>(null);
  const [ownerSearch, setOwnerSearch] = useState("");
  const [type, setType] = useState<DocumentType>(documentTypes[initialOwner][0]);
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
