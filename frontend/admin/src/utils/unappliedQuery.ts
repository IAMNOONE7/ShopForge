export const unappliedStatuses = ["Open", "Refunding", "Refunded", "ClosedByHand"] as const;
export type UnappliedStatus = typeof unappliedStatuses[number];
export type UnappliedQuery = { page: number; status: UnappliedStatus | "" };
export const unappliedPageSize = 25;
export const maxUnappliedPage = Math.floor(2_147_483_647 / unappliedPageSize) + 1;

export function unappliedQuery(parameters: URLSearchParams) {
  const rawPage = parameters.get("page") ?? "1";
  const number = Number(rawPage);
  const validPage = /^\d+$/.test(rawPage) && Number.isSafeInteger(number) && number >= 1 && number <= maxUnappliedPage;
  const rawStatus = parameters.get("status") ?? "Open";
  const status: UnappliedQuery["status"] = rawStatus === "" ? "" : unappliedStatuses.find((value) => value === rawStatus) ?? "Open";
  return { page: validPage ? number : 1, status, invalid: !validPage || status !== rawStatus };
}

export function unappliedParameters({ page, status }: UnappliedQuery) {
  const parameters = new URLSearchParams();
  if (page > 1) parameters.set("page", String(page));
  if (status !== "Open") parameters.set("status", status);
  return parameters;
}
