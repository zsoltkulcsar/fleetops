import { apiClient, type components } from "@fleetops/api-client";
import { checked, defectApi } from "./defectApi";

export const resultTypeFor = (type: string): components["schemas"]["DocumentType"] | undefined =>
  (({ inspection_hu: "hu_report", inspection_sp: "sp_report", tacho_check: "tacho_calibration" }) as const)[
    type as "inspection_hu" | "inspection_sp" | "tacho_check"
  ];
async function upload(file: File, vehicleId: string, type: components["schemas"]["DocumentType"]) {
  const link = await apiClient
    .POST("/api/v1/fleet/maintenance/documents/upload", {
      body: { vehicleId, type, contentType: file.type, sizeBytes: file.size },
    })
    .then(checked);
  const response = await fetch(link.url.url, {
    method: "PUT",
    headers: link.requiredHeaders,
    body: file,
    credentials: "omit",
  });
  if (!response.ok) throw new Error("upload_failed");
  return { fileId: link.fileId, page: "page" as const };
}
export async function completeMaintenance(repair: components["schemas"]["RepairDetail"], form: FormData) {
  const invoice = form.get("invoice");
  const result = form.get("result");
  const documentType = resultTypeFor(repair.type);
  let invoiceDocumentId: string | undefined;
  if (invoice instanceof File && invoice.size) {
    const file = await upload(invoice, repair.vehicleId, "maintenance_invoice");
    const document = await apiClient
      .POST("/api/v1/fleet/maintenance/invoices/confirm", {
        body: { vehicleId: repair.vehicleId, files: [file] },
      })
      .then(checked);
    invoiceDocumentId = document.id;
  }
  const resultDocument =
    documentType && result instanceof File && result.size
      ? {
          type: documentType,
          issuedAt: String(form.get("doneAt")),
          expiresAt: String(form.get("expiresAt")),
          number: null,
          files: [await upload(result, repair.vehicleId, documentType)],
        }
      : null;
  return defectApi.doneRepair(repair.id, {
    doneAt: String(form.get("doneAt")),
    odometerAtDoneKm: form.get("odometer") ? Number(form.get("odometer")) : null,
    costEur: form.get("cost") ? Number(form.get("cost")) : null,
    invoiceDocumentId,
    resultDocument,
  });
}
