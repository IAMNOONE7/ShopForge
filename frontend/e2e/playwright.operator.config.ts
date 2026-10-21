import { defineConfig } from "@playwright/test";
import { loadMerchantEnvironment } from "./support/merchantEnvironment.ts";

const environment = loadMerchantEnvironment();
export default defineConfig({
  testDir: "./operator-journeys", outputDir: "./artifacts/operator/results", forbidOnly: true,
  fullyParallel: false, workers: 1, retries: 0, timeout: 150_000, globalTimeout: 10 * 60_000,
  expect: { timeout: 15_000 },
  // HTML step titles retain filled passwords even when tracing is off. Keep operator reports to results and facts.
  reporter: [["list"], ["junit", { outputFile: "artifacts/operator/junit.xml" }]],
  use: {
    browserName: "chromium", headless: true, serviceWorkers: "block", actionTimeout: 10_000, navigationTimeout: 20_000,
    // Stack operator credentials and customer verification links must stay out of recordings and automatic screenshots.
    trace: "off", screenshot: "off", video: "off",
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
