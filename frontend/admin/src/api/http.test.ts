// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  HttpError,
  onUnauthorized,
  requestBlob,
  requestJson,
  saveDownload,
} from "./http";

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  vi.useRealTimers();
});

describe("HTTP transport", () => {
  it("preserves problem details, validation extensions, retry and trace metadata", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({
            title: "Invalid input",
            detail: "Server-owned detail",
            status: 422,
            errors: { Email: ["invalid"] },
            problems: ["missing-company"],
          }),
          {
            status: 422,
            headers: {
              "Content-Type": "application/problem+json",
              "X-Trace-Id": "header-trace",
              "Retry-After": "30",
            },
          },
        ),
      ),
    );

    const error = await requestJson("/api/test").catch(
      (reason: unknown) => reason,
    );
    expect(error).toBeInstanceOf(HttpError);
    expect(error).toMatchObject({
      kind: "http",
      status: 422,
      traceId: "header-trace",
      retryAfter: "30",
      problem: {
        title: "Invalid input",
        detail: "Server-owned detail",
        status: 422,
        errors: { Email: ["invalid"] },
        problems: ["missing-company"],
      },
    });
  });

  it("accepts empty success responses and rejects malformed or non-JSON payloads distinctly", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(
        new Response("{", { headers: { "Content-Type": "application/json" } }),
      )
      .mockResolvedValueOnce(
        new Response("<html>", { headers: { "Content-Type": "text/html" } }),
      );
    vi.stubGlobal("fetch", fetchMock);

    await expect(requestJson<void>("/api/empty")).resolves.toBeUndefined();
    await expect(requestJson("/api/malformed")).rejects.toMatchObject({
      kind: "invalid-response",
    });
    await expect(requestJson("/api/html")).rejects.toMatchObject({
      kind: "invalid-response",
    });
  });

  it("classifies network failures and notifies session listeners only for opted-in 401 responses", async () => {
    const listener = vi.fn();
    const unsubscribe = onUnauthorized(listener);
    const fetchMock = vi
      .fn()
      .mockRejectedValueOnce(new TypeError("offline"))
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(null, { status: 401 }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(requestJson("/api/offline")).rejects.toMatchObject({
      kind: "network",
      status: null,
    });
    await expect(requestJson("/api/private")).rejects.toMatchObject({
      status: 401,
    });
    await expect(
      requestJson("/api/session", { notifyUnauthorized: false }),
    ).rejects.toMatchObject({ status: 401 });
    expect(listener).toHaveBeenCalledTimes(1);
    unsubscribe();
  });

  it("checks PDF content, uses a safe filename, and revokes the object URL", async () => {
    vi.useFakeTimers();
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        new Response(new Blob(["pdf"]), {
          headers: {
            "Content-Type": "application/pdf; charset=binary",
            "Content-Disposition": 'attachment; filename="../invoice.pdf"',
          },
        }),
      )
      .mockResolvedValueOnce(
        new Response("not a pdf", {
          headers: { "Content-Type": "text/plain" },
        }),
      );
    vi.stubGlobal("fetch", fetchMock);
    const createObjectURL = vi.fn(() => "blob:test");
    const revokeObjectURL = vi.fn();
    Object.defineProperty(URL, "createObjectURL", {
      configurable: true,
      value: createObjectURL,
    });
    Object.defineProperty(URL, "revokeObjectURL", {
      configurable: true,
      value: revokeObjectURL,
    });
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(
      () => undefined,
    );

    const download = await requestBlob("/api/document", "fallback.pdf");
    expect(download.filename).toBe("invoice.pdf");
    saveDownload(download);
    expect(createObjectURL).toHaveBeenCalledWith(download.blob);
    await vi.runAllTimersAsync();
    expect(revokeObjectURL).toHaveBeenCalledWith("blob:test");
    await expect(
      requestBlob("/api/not-pdf", "fallback.pdf"),
    ).rejects.toMatchObject({ kind: "invalid-response" });
  });
});
