# Browser checks

This package runs Playwright against the real storefront and API. The default smoke checks cover boot,
store identity on two hostnames, Czech/English, phone/desktop navigation and product
page reloads. Browser contexts start without saved sessions. Browser writes are
blocked and fail the test. API responses are never replaced with fixtures.

The default checks are **read-only smoke checks**, separate from the isolated journeys below.
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

## Isolated shopper journeys

Use the backend-owned [end-to-end stack](../../README.md#the-end-to-end-stack), which
provides the E2E API, run setup, provider stand-ins, captured mail and read-only oracle.
Start it from the repository root and keep its services running during the checks:

```bash
tests/e2e/stack.sh up
cd frontend/e2e
npm run test:isolated
```

The runner reads API/rig origins from ignored `.e2e/stack.json` at the repository root.
`SHOPFORGE_E2E_STACK` can select another descriptor (relative to `frontend/e2e` or absolute).
Only plain HTTP loopback origins are accepted. Do not publish the descriptor: it also
contains operator credentials. The runner uses only its API/rig addresses.

Playwright builds and owns a separate storefront preview on port 5175, setting
`SHOPFORGE_API_TARGET` to the descriptor's API address and preserving the Host header.
It refuses to reuse an existing server; `SHOPFORGE_E2E_STOREFRONT_PORT` can choose a
different unprivileged port. The runner never starts, resets or stops the API/rig.
The read-only target file remains read-only and still rejects `mode: "isolated"`.

Each test verifies the API's E2E/database marker, creates exactly one run through
`POST /runs`, and validates its two distinct published hosts, listing/variant identities
and fresh stock readings. Both storefront proxies must resolve the returned store IDs
and the same API process marker. Only then may the browser write to the explicit cart,
checkout, registration, verification, sign-in/out and return-request routes on those
hosts, or submit the rig's payment form. Responses come from the real
services; routing only blocks writes outside that boundary. No provider credentials,
mail links or guest receipt tokens are copied into the run metadata attachment.

Four CS/EN phone/desktop projects exercise selected-variant guest checkout, Comgate
Pay/Decline/Leave pending, receipt recovery after decline and independent carts on
both hosts in one browser context. They assert checkout/receipt totals, exact variant,
payment attempt and stock facts, duplicate notification handling, captured order/paid
mail and a real invoice PDF download. A registered shopper verifies captured mail,
signs in, buys a variant, requests a return and downloads the credit note after parcel
receipt and refund. The oracle and stand-in verify exact variant/amount, restored stock
and provider refund. Logout and the second host show the private-order sign-in prompt,
with no document controls.
The run owner's authenticated API session performs only the required acceptance and
parcel-receipt progression, with one keyed receipt request and no automatic write
retry. This is fixture progression, not merchant browser coverage. Owner credentials
are validated against the run and never attached to run metadata. Verification links
keep the mailed host/path/token and map that host to the local preview port.
All money and mail stay in the disposable stack.
Payment recovery cases drop a notification, fail the provider status lookup or return
an unrecognised status. The receipt must stay awaiting payment with reserved stock,
no invoice and no paid mail. A later successful status read, delivered twice, updates
the same receipt through real GET polling and deducts stock once, without another
checkout or browser POST. The rig's recorded push outcomes and read-only oracle prove
the intermediate and recovered states. Only per-run provider controls are used;
these cases do not certify scheduled reconciliation or reservation expiry.
Each test ends only its own run via `DELETE /runs/{run}` and verifies it is forgotten.
This suspends the tenant; its database rows remain until the stack is taken down.

The normal matrix has a ten-minute deadline, one worker and zero retries.
`npm run test:isolated:repeat` repeats every case twenty times, stops on the first
failure and has a 200-minute deadline (twenty ten-minute matrices). To focus a repeat,
append e.g. `-- --project=en-desktop --grep 'payment recovery'`.
Every repetition creates its own run; a failed test is never retried. Reports, per-run
identities and order/stock/mail facts are under ignored `artifacts/isolated/`; use
`npx playwright show-report artifacts/isolated/report` to inspect them. Failure traces,
videos and screenshots are retained. Reports remain private because network records
can contain guest receipt tokens. Save reports before another invocation replaces them.

When finished, run `tests/e2e/stack.sh down` from the repository root to stop only the
disposable stack and discard its data. Lost checkout/return answers, merchant/operator
browser flows, refund uncertainty, scheduled recovery, complete repeat acceptance,
CI wiring and DNS remain separate work.
These journeys do not advance the stack-wide clock or run scheduled passes. Public
credited amounts remain distinct from provider settlement; settlement is asserted
through the current oracle and stand-in rather than inferred from the shopper label.
