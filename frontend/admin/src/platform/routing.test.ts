import { expect, it } from "vitest";
import { isOperatorPath, operatorHomePath } from "./routing";
import { safeAdminReturnPath } from "../routing";
it.each(["/", "/admin/"])("keeps the operator entry and merchant return paths separate under %s", base => {
  const prefix = base === "/" ? "" : "/admin";
  expect(operatorHomePath(base)).toBe(`${prefix}/platform/tenants`);
  expect(isOperatorPath(`${prefix}/platform`, base)).toBe(true); expect(isOperatorPath(`${prefix}/platform/tenants`, base)).toBe(true); expect(isOperatorPath(`${prefix}/platform/unknown`, base)).toBe(true);
  for (const path of [`${prefix}/platformish`, `${prefix}/products`, "//external.example/platform"]) expect(isOperatorPath(path, base)).toBe(false);
  expect(safeAdminReturnPath(`${prefix}/platform/tenants?value=private`, base)).toBe(`${prefix}/products`);
  expect(safeAdminReturnPath(`${prefix}/stores/one`, base)).toBe(`${prefix}/stores/one`);
});
