export const maximumPhotoBytes = 1_500_000;

export async function compressPhoto(file: Blob, cropPercent = 0): Promise<Blob> {
  if (
    !["image/jpeg", "image/png"].includes(file.type) ||
    file.size > 20 * 1024 * 1024 ||
    !Number.isFinite(cropPercent) ||
    cropPercent < 0 ||
    cropPercent > 20
  ) {
    throw new Error("invalid_photo");
  }
  const bitmap = await createImageBitmap(file);
  try {
    const insetX = Math.round((bitmap.width * cropPercent) / 100);
    const insetY = Math.round((bitmap.height * cropPercent) / 100);
    const width = bitmap.width - insetX * 2;
    const height = bitmap.height - insetY * 2;
    const canvas = document.createElement("canvas");
    const context = canvas.getContext("2d");
    if (!context || width < 1 || height < 1) throw new Error("image_unavailable");
    let scale = Math.min(1, 2400 / Math.max(width, height));
    for (let attempt = 0; attempt < 10; attempt += 1) {
      canvas.width = Math.max(1, Math.round(width * scale));
      canvas.height = Math.max(1, Math.round(height * scale));
      context.drawImage(bitmap, insetX, insetY, width, height, 0, 0, canvas.width, canvas.height);
      const compressed = await new Promise<Blob>((resolve, reject) =>
        canvas.toBlob(
          (blob) => (blob ? resolve(blob) : reject(new Error("image_unavailable"))),
          "image/jpeg",
          0.82,
        ),
      );
      if (compressed.size <= maximumPhotoBytes) return compressed;
      scale *= 0.7;
    }
    throw new Error("photo_too_large");
  } finally {
    bitmap.close();
  }
}

export async function photoPayload(blob: Blob): Promise<string> {
  const bytes = new Uint8Array(await blob.arrayBuffer());
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += 8192) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 8192));
  }
  return btoa(binary);
}

export async function ensurePhotoSpace(bytes: number): Promise<void> {
  if (navigator.storage?.persist) {
    try {
      await navigator.storage.persist();
    } catch {
      /* Browser may refuse persistence. */
    }
  }
  const estimate = await navigator.storage?.estimate?.();
  // Base64, encryption and the atomic replacement need headroom above blob size.
  if (
    estimate?.quota !== undefined &&
    estimate.usage !== undefined &&
    estimate.quota - estimate.usage < bytes * 4 + 1_000_000
  ) {
    throw new Error("photo_storage_low");
  }
}
