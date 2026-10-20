import { defineConfig } from "@playwright/test";
import { loadMerchantEnvironment } from "./support/merchantEnvironment.ts";

const environment = loadMerchantEnvironment();
export default defineConfig({
  testDir: "./merchant-journeys", outputDir: "./artifacts/merchant/results", forbidOnly: true,
  fullyParallel: false, workers: 1, retries: 0, timeout: 180_000, globalTimeout: 10 * 60_000,
  expect: { timeout: 15_000 },
  reporter: [["list"], ["html", { outputFolder: "artifacts/merchant/report", open: "never" }], ["junit", { outputFile: "artifacts/merchant/junit.xml" }]],
  use: {
    browserName: "chromium", headless: true, serviceWorkers: "block", actionTimeout: 10_000, navigationTimeout: 20_000,
    // Merchant sign-in and credential forms must not enter trace network bodies or DOM snapshots.
    trace: "off", screenshot: "only-on-failure", video: "off",
  },
  projects: ["cs", "en"].flatMap(language => [
    { name: `${language}-phone`, use: { locale: `${language}-${language === "cs" ? "CZ" : "GB"}`, viewport: { width: 320, height: 900 }, hasTouch: true } },
    { name: `${language}-desktop`, use: { locale: `${language}-${language === "cs" ? "CZ" : "GB"}`, viewport: { width: 1280, height: 900 } } },
  ]),
  webServer: [
    { command: `npm run build && npm run preview -- --host 127.0.0.1 --port ${environment.storefrontPort} --strictPort`, cwd: "../storefront", url: environment.frontendOrigin,
      reuseExistingServer: false, timeout: 90_000, env: { SHOPFORGE_API_TARGET: environment.apiOrigin }, stdout: "ignore", stderr: "pipe", gracefulShutdown: { signal: "SIGTERM", timeout: 5_000 } },
    { command: `npm run build && npm run preview -- --host 127.0.0.1 --port ${new URL(environment.adminOrigin).port} --strictPort`, cwd: "../admin", url: `${environment.adminOrigin}/admin/`,
      reuseExistingServer: false, timeout: 90_000, env: { SHOPFORGE_API_TARGET: environment.apiOrigin }, stdout: "ignore", stderr: "pipe", gracefulShutdown: { signal: "SIGTERM", timeout: 5_000 } },
  ],
});
