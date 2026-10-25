import { describe, expect, it } from "vitest";
import { HttpError } from "../api/http";
import { listingVisibilityMessages } from "./listingVisibility";

describe("visibility refusal messages", () => {
  it("keeps exact SKU/listing text from the matching field and deduplicates it", () => {
    const message = "Still without them: CHAIR-<script>literal</script>.";
    const error = new HttpError("http", 400, { errors: { IsVisible: [message, message], price: ["Another field"] } });
    expect(listingVisibilityMessages(error, "isVisible")).toEqual([message]);
    expect(listingVisibilityMessages(error, "storeProductIds")).toEqual([]);
  });
  it.each([null, "", 42, {}, ["", null, 42, {}]])("ignores malformed field messages: %j", messages => {
    const error = new HttpError("http", 400, { errors: { isVisible: messages } } as never);
    expect(listingVisibilityMessages(error, "isVisible")).toEqual([]);
  });
  it.each([new HttpError("network", null), new HttpError("invalid-response", 400), new HttpError("http", 503), new Error()])("does not present uncertainty as a visibility refusal: %j", error => {
    expect(listingVisibilityMessages(error, "isVisible")).toEqual([]);
  });
});
