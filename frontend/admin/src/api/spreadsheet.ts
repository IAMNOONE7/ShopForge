import { HttpError, requestBlob } from "./http";

export const spreadsheetContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
export async function readSpreadsheet(path: string, fallbackFilename: string, signal?: AbortSignal) {
  const download = await requestBlob(path, fallbackFilename, { signal, expectedContentType: spreadsheetContentType });
  signal?.throwIfAborted();
  const signature = new Uint8Array(await download.blob.slice(0, 4).arrayBuffer());
  signal?.throwIfAborted();
  // XLSX is a ZIP container. Reject empty responses and branded HTML before offering a file.
  if (signature.length !== 4 || signature[0] !== 0x50 || signature[1] !== 0x4b || signature[2] !== 3 || signature[3] !== 4) throw new HttpError("invalid-response", 200);
  return { ...download, filename: /\.xlsx$/i.test(download.filename) ? download.filename : fallbackFilename };
}
