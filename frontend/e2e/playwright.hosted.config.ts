import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./hosted-smoke", outputDir: "./artifacts/hosted/results", forbidOnly: true,
  fullyParallel: false, workers: 1, retries: 0, timeout: 90_000, globalTimeout: 10 * 60_000,
  expect: { timeout: 10_000 }, reporter: [["list"], ["junit", { outputFile: "artifacts/hosted/junit.xml" }]],
  use: { browserName: "chromium", headless: true, serviceWorkers: "block", actionTimeout: 10_000, navigationTimeout: 20_000,
    trace: "off", screenshot: "off", video: "off" },
  projects: ["cs", "en"].flatMap(language => [
    { name: `${language}-phone`, use: { locale: `${language}-${language === "cs" ? "CZ" : "GB"}`, viewport: { width: 320, height: 900 }, hasTouch: true } },
    { name: `${language}-desktop`, use: { locale: `${language}-${language === "cs" ? "CZ" : "GB"}`, viewport: { width: 1280, height: 900 } } },
  ]),
});
