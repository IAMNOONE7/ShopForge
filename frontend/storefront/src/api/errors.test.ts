import { describe, expect, it } from "vitest";
import { errorMessageKey, fieldIssues } from "./errors";
import { HttpError } from "./http";

describe("localized error mapping", () => {
  it("maps status and operation without matching server sentences", () => {
    expect(errorMessageKey(new HttpError("http", 403), "write")).toBe(
      "permissionDenied",
    );
    expect(errorMessageKey(new HttpError("http", 401), "login")).toBe(
      "credentials",
    );
    expect(errorMessageKey(new HttpError("http", 409), "write")).toBe(
      "conflict",
    );
    expect(errorMessageKey(new HttpError("http", 429), "read")).toBe(
      "rateLimited",
    );
  });

  it("maps nested and dynamic validation fields to focusable controls", () => {
    const error = new HttpError("http", 422, {
      errors: {
        Email: ["server text is not used"],
        BillingAddress: ["invalid"],
        "Values.material": ["invalid"],
        "f.length": ["invalid"],
      },
    });
    expect(fieldIssues(error)).toEqual([
      { field: "Email", target: "email", key: "emailInvalid" },
      {
        field: "BillingAddress",
        target: "billing.fullName",
        key: "addressInvalid",
      },
      {
        field: "Values.material",
        target: "attribute:material",
        key: "valueInvalid",
      },
      { field: "f.length", target: "f.length", key: "filterInvalid" },
    ]);
  });
});
