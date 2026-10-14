import { defineConfig } from "@playwright/test";
import { loadEnvironment } from "./support/environment.ts";

const environment = loadEnvironment();

export default defineConfig({
  testDir: "./specs",
  globalSetup: "./support/globalSetup.ts",
  outputDir: "./artifacts/results",
  forbidOnly: true,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 30_000,
  globalTimeout: 5 * 60_000,
  expect: { timeout: 10_000 },
  reporter: [["list"], ["html", { outputFolder: "artifacts/report", open: "never" }], ["junit", { outputFile: "artifacts/junit.xml" }]],
  use: {
    browserName: "chromium",
    headless: true,
    serviceWorkers: "block",
    actionTimeout: 10_000,
    navigationTimeout: 15_000,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },
  projects: ["cs", "en"].flatMap((language) => [
    { name: `${language}-phone`, use: { locale: `${language}-${language === "cs" ? "CZ" : "GB"}`, viewport: { width: 320, height: 900 }, hasTouch: true } },
    { name: `${language}-desktop`, use: { locale: `${language}-${language === "cs" ? "CZ" : "GB"}`, viewport: { width: 1280, height: 900 } } },
  ]),
  webServer: process.env.SHOPFORGE_E2E_START_STOREFRONT === "1" ? {
    command: "npm run build && npm run preview -- --host 127.0.0.1 --port 5173 --strictPort",
    cwd: "../storefront",
    url: "http://127.0.0.1:5173",
    reuseExistingServer: false,
    timeout: 60_000,
    env: { SHOPFORGE_API_TARGET: environment.apiOrigin },
    stdout: "ignore",
    stderr: "pipe",
    gracefulShutdown: { signal: "SIGTERM", timeout: 5_000 },
  } : undefined,
});
