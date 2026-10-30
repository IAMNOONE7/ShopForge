import { expect, type Page, type Request } from "@playwright/test";
import { createHash } from "node:crypto";

type Asset = { path: string; sha256: string };
type AssetFacts = { assets: Asset[]; failures: string[]; checks: Promise<void>[] };

export function expectAssetHeaders(headers: Record<string, string>, path: string) {
  expect(headers["cache-control"]).toBe("public, max-age=31536000, immutable");
  expect(headers["content-type"]).toMatch(path.endsWith(".css") ? /^text\/css\b/ : /^(?:text|application)\/javascript\b/);
}

export function observeHostedAssets(page: Page, facts: AssetFacts, buildFile: (path: string) => Buffer) {
  let documentVersion = 0;
  const documents = new WeakMap<Request, number>();
  page.on("framenavigated", frame => { if (frame === page.mainFrame()) documentVersion++; });
  page.on("request", request => { documents.set(request, documentVersion); });
  page.on("requestfailed", request => {
    const path = new URL(request.url()).pathname;
    if (/\.(?:js|css)$/.test(path) && request.failure()?.errorText !== "net::ERR_ABORTED") facts.failures.push(path);
  });
  page.on("response", response => {
    const path = new URL(response.url()).pathname;
    if (!/^(?:\/admin)?\/assets\/[^/]+\.(?:js|css)$/.test(path)) return;
    const request = response.request(), requestedDocument = documents.get(request) ?? documentVersion;
    facts.checks.push((async () => {
      try {
        expect(response.status()).toBe(200); expectAssetHeaders(response.headers(), path);
        let body: Buffer;
        try { body = await response.body(); }
        catch (error) {
          // Navigation can discard an unread response; observed headers and complete bodies still get checked.
          if (request.failure()?.errorText === "net::ERR_ABORTED" || page.isClosed() || requestedDocument !== documentVersion) return;
          throw error;
        }
        const sha256 = createHash("sha256").update(body).digest("hex");
        expect(sha256).toBe(createHash("sha256").update(buildFile(path)).digest("hex"));
        facts.assets.push({ path, sha256 });
      } catch { facts.failures.push(path); }
    })());
  });
}
