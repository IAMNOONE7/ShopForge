import { mkdirSync, writeFileSync } from "node:fs";
import { expect, request } from "@playwright/test";
import { loadEnvironment } from "./environment.ts";

export default async function globalSetup() {
  const environment = loadEnvironment();
  const client = await request.newContext({ timeout: 3_000 });
  const evidence: { origin: string; id: string; name: string }[] = [];
  const healthy: string[] = [];
  mkdirSync("artifacts", { recursive: true });
  try {
    for (const path of ["/health/live", "/health/ready"]) {
      await expect.poll(async () => {
        try { return (await client.get(environment.apiOrigin + path, { maxRedirects: 0 })).status(); }
        catch { return 0; }
      }, { message: `API ${path} must return 200`, timeout: 20_000, intervals: [250, 500, 1_000] }).toBe(200);
      healthy.push(path);
    }
    for (const target of environment.stores) {
      await expect.poll(async () => {
        try {
          const response = await client.get(target.origin + "/api/storefront/store", { maxRedirects: 0 });
          if (response.status() !== 200 || !response.headers()["content-type"]?.includes("application/json")) return null;
          const store = await response.json() as { id?: string; name?: string };
          if (store.id === target.id && typeof store.name === "string" && store.name.trim()) {
            evidence.push({ origin: target.origin, id: store.id, name: store.name });
            return store.id;
          }
          return null;
        } catch { return null; }
      }, { message: `${target.origin} must resolve the configured store ID through the real API`, timeout: 20_000, intervals: [250, 500, 1_000] }).toBe(target.id);
      const response = await client.get(target.origin + "/", { maxRedirects: 0 });
      expect(response.status(), `Storefront HTML at ${target.origin}`).toBe(200);
      expect(response.headers()["content-type"]).toContain("text/html");
    }
  } finally {
    writeFileSync("artifacts/environment.json", JSON.stringify({
      mode: environment.mode, apiOrigin: environment.apiOrigin, targets: environment.stores,
      healthy, verifiedStores: evidence,
    }, null, 2));
    await client.dispose();
  }
}
