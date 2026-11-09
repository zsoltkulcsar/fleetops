import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Drawer } from "@fleetops/ui";
import { DriverRequestError, scheduleApi } from "./scheduleApi";
import { driverDate } from "./DriverSchedule";

export async function openFreshFile(documentId: string, fileId: string) {
  const target = window.open("", "_blank");
  if (target) target.opener = null;
  try {
    const result = await scheduleApi.file(documentId, fileId);
    const url = new URL(result.url);
    if (url.protocol !== "https:" && !(import.meta.env.DEV && url.protocol === "http:"))
      throw new Error("unsafe_file_link");
    if (target) target.location.href = url.href;
    else window.location.assign(url.href);
  } catch (error) {
    target?.close();
    throw error;
  }
}

export function DriverTruck({ id, online, onClose }: { id: string; online: boolean; onClose: () => void }) {
  const [failure, setFailure] = useState("");
  const [opening, setOpening] = useState(false);
  const truck = useQuery({
    queryKey: ["driver-truck", id],
    queryFn: () => scheduleApi.vehicle(id),
    enabled: online,
    gcTime: 0,
    staleTime: 0,
  });
  const viewFile = async (documentId: string, fileId: string) => {
    setOpening(true);
    setFailure("");
    try {
      await openFreshFile(documentId, fileId);
    } catch {
      setFailure("The photo could not be opened. Try again while online.");
    } finally {
      setOpening(false);
    }
  };
  return (
    <Drawer
      open
      title={truck.data?.plate ?? "Open on this truck"}
      closeLabel="Close truck details"
      onClose={onClose}
    >
      <div className="driver-sheet">
        {!online ? (
          <p>Truck details and photos need a connection.</p>
        ) : truck.isPending ? (
          <p role="status">Loading truck details…</p>
        ) : truck.isError ? (
          <div role="alert">
            <p>
              {truck.error instanceof DriverRequestError && [403, 404].includes(truck.error.status)
                ? "This truck is not in your current schedule."
                : "Truck details could not be loaded."}
            </p>
            <button onClick={() => void truck.refetch()}>Try again</button>
          </div>
        ) : (
          truck.data && (
            <>
              <h3>Open items</h3>
              {truck.data.items.length ? (
                <ul className="driver-document-list">
                  {truck.data.items.map((item) => (
                    <li key={item.id}>
                      <div>
                        <strong>{item.category.replaceAll("_", " ")}</strong>
                        <span>
                          {item.severity} · {driverDate(item.date)}
                        </span>
                      </div>
                      {item.documentId && item.fileId && (
                        <button
                          disabled={opening}
                          onClick={() => void viewFile(item.documentId!, item.fileId!)}
                        >
                          Open photo
                        </button>
                      )}
                    </li>
                  ))}
                </ul>
              ) : (
                <p>No open items on this truck.</p>
              )}
              <h3>Truck documents</h3>
              {truck.data.documents.length ? (
                <ul className="driver-document-list">
                  {truck.data.documents.map((document) => (
                    <li key={document.id}>
                      <div>
                        <strong>{document.type.replaceAll("_", " ")}</strong>
                        <span>
                          {document.status}
                          {document.expiresAt ? ` · Expires ${driverDate(document.expiresAt)}` : ""}
                        </span>
                        {document.files.map((file) => (
                          <button
                            key={file.id}
                            disabled={opening}
                            onClick={() => void viewFile(document.id, file.id)}
                          >
                            Open {file.page === "page" ? "file" : file.page}
                          </button>
                        ))}
                      </div>
                    </li>
                  ))}
                </ul>
              ) : (
                <p>No documents to show.</p>
              )}
            </>
          )
        )}
        {failure && (
          <p className="driver-error" role="alert">
            {failure}
          </p>
        )}
      </div>
    </Drawer>
  );
}
