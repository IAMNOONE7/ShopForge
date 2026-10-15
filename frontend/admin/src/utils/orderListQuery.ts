export const orderPageSize = 25;
export const maxOrderPage = Math.floor(2_147_483_647 / orderPageSize) + 1;
export const orderSorts = ["-placed", "placed", "status", "-status"] as const;
export type OrderSort = typeof orderSorts[number];
export type OrderListQuery = { page: number; q: string; sort: OrderSort };

export function orderListQuery(parameters: URLSearchParams) {
  const rawPage = parameters.get("page") ?? "1";
  const number = Number(rawPage);
  const invalidPage = !/^\d+$/.test(rawPage) || !Number.isSafeInteger(number) || number < 1 || number > maxOrderPage;
  const rawSort = parameters.get("sort") ?? "-placed";
  const sort = orderSorts.find((value) => value === rawSort) ?? "-placed";
  const text = (parameters.get("q") ?? "").trim();
  return { page: invalidPage ? 1 : number, sort, q: text.length >= 2 ? text : "", shortQuery: text.length === 1, invalidOptions: invalidPage || sort !== rawSort };
}

export function orderListParameters({ page, q, sort }: OrderListQuery) {
  const parameters = new URLSearchParams();
  if (page > 1) parameters.set("page", String(page));
  if (q) parameters.set("q", q);
  if (sort !== "-placed") parameters.set("sort", sort);
  return parameters;
}
