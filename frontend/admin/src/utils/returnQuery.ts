export const returnStatuses = ["Requested", "Accepted", "Refused", "Received"] as const;
export const refundStatuses = ["Pending", "Refunded", "Failed", "ByStore", "ClosedByHand"] as const;
export type ReturnQuery = { status: typeof returnStatuses[number] | ""; refund: typeof refundStatuses[number] | "" };
export const returnLimit = 200;

export function returnQuery(parameters: URLSearchParams) {
  const rawStatus = parameters.get("status") ?? "";
  const rawRefund = parameters.get("refund") ?? "";
  const status: ReturnQuery["status"] = returnStatuses.find((value) => value === rawStatus) ?? "";
  const refund: ReturnQuery["refund"] = refundStatuses.find((value) => value === rawRefund) ?? "";
  return { status, refund, invalid: status !== rawStatus || refund !== rawRefund };
}

export function returnParameters({ status, refund }: ReturnQuery) {
  const parameters = new URLSearchParams();
  if (status) parameters.set("status", status);
  if (refund) parameters.set("refund", refund);
  return parameters;
}

export function returnReviewPath(storeId: string, id: string, orderNumber: string, query: ReturnQuery = { status: "", refund: "" }) {
  const parameters = returnParameters(query);
  parameters.set("order", orderNumber);
  return `/stores/${encodeURIComponent(storeId)}/returns/${encodeURIComponent(id)}?${parameters}`;
}
