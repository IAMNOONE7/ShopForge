# Browser smoke tests

This package runs Playwright against the real storefront and API. It checks boot,
store identity on two hostnames, Czech/English, phone/desktop navigation and product
page reloads. Browser contexts start without saved sessions. Browser writes are
blocked and fail the test. API responses are never replaced with fixtures.

These are **read-only smoke checks**, not isolated checkout/payment/return journeys.
They can use existing demo stores and do not seed, reset or clean up business data.
The target file is frontend runner input, not a backend endpoint or fixture contract.

## Run locally

Start the documented compose dependencies and API separately. Install both packages:

```bash
cd frontend/storefront && npm ci
cd ../e2e && npm ci
npm run install:browser
```

On Linux, the browser may need system libraries: `npx playwright install --with-deps chromium`.
For containers/CI use the Playwright image matching the lockfile version, or install
its documented browser dependencies. The harness does not install OS packages.

Create ignored `frontend/e2e/targets.local.json` with the actual IDs returned by
`GET /api/storefront/store` on each intended hostname. For example, with the dev
storefront running, `curl http://shop-a.localhost:5173/api/storefront/store` and
`curl http://shop-b.localhost:5173/api/storefront/store`. Do not copy guessed IDs:

```json
{
  "mode": "read-only",
  "apiOrigin": "http://127.0.0.1:5080",
  "stores": [
    { "origin": "http://shop-a.localhost:5173", "id": "<first store UUID>" },
    { "origin": "http://shop-b.localhost:5173", "id": "<second store UUID>" }
  ]
}
```

The targets must be different hostnames and different IDs. Ports alone share cookie
scope and do not qualify. The configured stores need published, nonempty catalogs.
Each browser project uses both hosts inside one context; a passing check demonstrates
host resolution, not authenticated session/cart isolation or independent DB fixtures.

From `frontend/e2e`:

```bash
npm run test:unit
npm run lint
npm run typecheck
SHOPFORGE_E2E_TARGETS=targets.local.json npm test
```

With no frontend server running, add `SHOPFORGE_E2E_START_STOREFRONT=1` to let Playwright
build the storefront and start/stop its production preview on port 5173. It refuses
to reuse a server already on that port. The Vite proxy uses `apiOrigin` through `SHOPFORGE_API_TARGET` and preserves
the storefront Host header. Otherwise start the storefront yourself, with
`SHOPFORGE_API_TARGET` set if the API uses another address. The API is never started,
stopped or reset by this package. An externally hosted frontend can use its own origins.

The API's `/health/live` and `/health/ready`, both store identity reads, and HTML are
checked before browsers run. Readiness polling, actions, navigation, tests and the
whole run have deadlines. Runs use one worker and zero retries. To inspect repeat
stability, `npm run test:repeat` runs each smoke project twenty times; failures stay
failures and are not retried. Its five-minute total deadline is intentional.
Repeated reads share the API's per-client limits with other local users and can fail
with HTTP 429 on the shared stack. Run one smoke matrix after the window clears;
repeat-stability runs need a dedicated test environment with an agreed rate budget.
The harness does not disable rate limiting, retry product reads, or retry failed tests.

Reports live under ignored `artifacts/`: HTML (`npm run report`), JUnit, environment
identities, and per-test browser errors. Failures retain traces, video and screenshots.
Uncaught exceptions, attempted writes, unexpected API errors and duplicate React
child keys fail smoke checks. Only GET `/api/storefront/account/me` 401 is expected
for the fresh guest context. Console errors are attached for review, including those
guest-session 401s. The boot test does not certify catalog uniqueness or other business invariants;
those need explicit assertions in the isolated journeys.
`npx playwright show-trace artifacts/results/<failed-test>/trace.zip` opens a trace.
Treat reports as private: browser snapshots/network records can contain store data.
CI should install this package/browser, supply targets and running services, run the
checks above and upload `artifacts/` even on failure. No CI workflow is changed here.

## Isolated journey prerequisites

The current root compose file has PostgreSQL, Azurite and telemetry only. The API
seeds development demo data. `FakeGateway`, `FakeComgate`, in-memory secret storage
and payment simulations are backend integration-test helpers, not browser-reachable
compose services. Supplying `mode: "isolated"` is refused rather than granting writes.

Before checkout/return or merchant/operator journeys can be enabled, the backend-owned
test environment must provide:

- A disposable database/storage dataset, per-run tenant/store identities and hosts,
  deterministic setup/reset and cleanup that cannot target the shared demo dataset.
- Browser-reachable payment/refund controls for success, refusal, delay, no answer
  and unknown status, with no live provider credentials or calls.
- Test-only writable secret storage, controlled DNS and verification-mail/document access.
- A read-only oracle for stock, orders, attempts, returns and credits, and a way to
  establish that the application, controls and oracle belong to the same isolated run.
- A compose/API/CI entry point that publishes the real transport/URLs and fails outside
  its explicit test environment.

Those transports are not implemented or assumed here. After they exist, add the actual
setup/teardown fixture and server-state assertions before permitting money/data writes.
Response interception or an asserted `runId` in a local file is not data isolation.
