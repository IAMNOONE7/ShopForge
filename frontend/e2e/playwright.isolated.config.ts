import { defineConfig } from "@playwright/test";
import { loadStack } from "./support/isolatedEnvironment.ts";

const environment = loadStack();
export default defineConfig({
  testDir: "./journeys",
  outputDir: "./artifacts/isolated/results",
  forbidOnly: true,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 90_000,
  globalTimeout: 10 * 60_000,
  expect: { timeout: 15_000 },
  reporter: [["list"], ["html", { outputFolder: "artifacts/isolated/report", open: "never" }], ["junit", { outputFile: "artifacts/isolated/junit.xml" }]],
  use: {
    browserName: "chromium", headless: true, serviceWorkers: "block",
    actionTimeout: 10_000, navigationTimeout: 20_000,
    trace: "retain-on-failure", screenshot: "only-on-failure", video: "retain-on-failure",
  },
  projects: ["cs", "en"].flatMap(language => [
    { name: `${language}-phone`, use: { locale: `${language}-${language === "cs" ? "CZ" : "GB"}`, viewport: { width: 320, height: 900 }, hasTouch: true } },
    { name: `${language}-desktop`, use: { locale: `${language}-${language === "cs" ? "CZ" : "GB"}`, viewport: { width: 1280, height: 900 } } },
  ]),
  webServer: {
    command: `npm run build && npm run preview -- --host 127.0.0.1 --port ${environment.storefrontPort} --strictPort`,
    cwd: "../storefront", url: environment.frontendOrigin, reuseExistingServer: false, timeout: 90_000,
    env: { SHOPFORGE_API_TARGET: environment.apiOrigin },
    stdout: "ignore", stderr: "pipe", gracefulShutdown: { signal: "SIGTERM", timeout: 5_000 },
  },
});
