import { requestBlob, saveDownload } from "./http";

export async function downloadGuestDocument(
  orderNumber: string,
  documentNumber: string,
  token: string,
) {
  const parameters = new URLSearchParams({ token });
  saveDownload(
    await requestBlob(
      `/api/storefront/orders/${encodeURIComponent(orderNumber)}/documents/${encodeURIComponent(documentNumber)}?${parameters}`,
      `${documentNumber}.pdf`,
    ),
  );
}

export async function downloadAccountDocument(
  orderNumber: string,
  documentNumber: string,
) {
  saveDownload(
    await requestBlob(
      `/api/storefront/account/orders/${encodeURIComponent(orderNumber)}/documents/${encodeURIComponent(documentNumber)}`,
      `${documentNumber}.pdf`,
    ),
  );
}
