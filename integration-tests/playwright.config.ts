// Config file for integration tests
import { PlaywrightTestConfig, devices } from "@playwright/test";

const config: PlaywrightTestConfig = {
  testDir: ".",
  testMatch: "test-playwright.*",
  expect: {
    // timeout: 5000,
  },
  timeout: 15000,
  use: {
    // actionTimeout: 1000,
    headless: true,
    // Ideally this would be retain-on-failure, but it fails sometimes
    trace: "on",
    screenshot: "off",
    video: "on",
  },
  reporter: [
    ["list"],
    ["json", { outputFile: "rundir/test_results/integration_tests.json" }],
    ["xml", { outputFile: "rundir/test_results/integration_tests.xml" }],
  ],
};

export default config;
