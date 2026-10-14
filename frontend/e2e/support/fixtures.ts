import { test as base, expect } from "@playwright/test";
import { writeFileSync } from "node:fs";
import { loadEnvironment, type SmokeEnvironment } from "./environment.ts";

export const test = base.extend<{ environment: SmokeEnvironment; readOnly: void }>({
  // oxlint-disable-next-line no-empty-pattern -- Playwright requires an object pattern for fixture dependencies.
  environment: async ({}, use) => { await use(loadEnvironment()); },
  readOnly: [async ({ context }, use, testInfo) => {
    const blocked: string[] = [];
    const pageErrors: string[] = [];
    const consoleErrors: string[] = [];
    const failedReads: string[] = [];
    context.on("page", (page) => {
      page.on("pageerror", (error) => pageErrors.push(error.message));
      page.on("console", (message) => { if (message.type() === "error") consoleErrors.push(message.text()); });
      page.on("response", (response) => {
        const path = new URL(response.url()).pathname;
        const expectedGuest = path === "/api/storefront/account/me" && response.status() === 401 && response.request().method() === "GET";
        if (path.startsWith("/api/") && response.status() >= 400 && !expectedGuest) {
          failedReads.push(`${response.status()} ${response.request().method()} ${path}`);
        }
      });
    });
    await context.route("**/*", async (route) => {
      const request = route.request();
      if (!["GET", "HEAD", "OPTIONS"].includes(request.method())) {
        blocked.push(`${request.method()} ${new URL(request.url()).pathname}`);
        await route.abort("blockedbyclient");
      } else await route.continue();
    });
    await use();
    const errorsPath = testInfo.outputPath("browser-errors.json");
    writeFileSync(errorsPath, JSON.stringify({ blockedWrites: blocked, pageErrors, consoleErrors, failedReads }, null, 2));
    await testInfo.attach("browser-errors", { path: errorsPath, contentType: "application/json" });
    expect(blocked, "Read-only smoke attempted a write").toEqual([]);
    expect(pageErrors, "Uncaught browser errors").toEqual([]);
    expect(failedReads, "Unexpected API errors").toEqual([]);
    expect(consoleErrors.filter((message) => message.includes("Encountered two children with the same key")), "Duplicate React child keys").toEqual([]);
  }, { auto: true }],
});
export { expect };
