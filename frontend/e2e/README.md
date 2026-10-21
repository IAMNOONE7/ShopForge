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

## API-hosted production smoke

`npm run test:hosted` checks the actual API serving a packaged storefront at `/` and
admin at `/admin/`. It starts no Vite server or proxy and never replaces responses.
Use a disposable E2E API and its matching rig, with the normal published API package
containing `frontends/storefront/` and `frontends/admin/` (each including its built
`index.html` and `assets/`). Build the apps with their existing `npm run build`
commands and package the resulting `dist` directories unchanged. The backend's
`Frontends:Root` can select that directory when rehearsing outside the package.

Configure `Frontends:AdminHost=admin.localhost` and
`Frontends:EdgeHost=edge.localhost` on that API before starting it. The runner uses
the same port as the descriptor's API for every hostname. For different configured
`.localhost` names, set `SHOPFORGE_E2E_HOSTED_ADMIN_HOST` and
`SHOPFORGE_E2E_HOSTED_EDGE_HOST` to match. Both must be distinct; public targets are
refused. Supply the matching API/rig descriptor and the absolute packaged frontend
directory, from `frontend/e2e`:

```bash
SHOPFORGE_E2E_STACK=/absolute/path/to/.e2e/stack.json \
SHOPFORGE_E2E_HOSTED_ROOT=/absolute/path/to/package/frontends \
PLAYWRIGHT_BROWSERS_PATH=/tmp/shopforge-playwright npm run test:hosted
```

The suite verifies the E2E/database marker, admin-host root redirects and package
entry bytes before creating run data. Each case then owns a fresh rig run and cleans
it up. Checks cover host restrictions, extensionless deep links, fallback exclusions,
crawler responses, exact CSP/referrer/cache headers, MIME types, asset revalidation,
and entry/lazy JS/CSS bytes against the package. Browser cases use CS/EN at 320/1280px,
reload product and authenticated order/domain routes, load the operator entry, and
check same-context cart and admin-cookie host isolation. The only browser writes
allowed are adding one exact run-owned variant to a cart and that run owner's login.
No checkout, payment, DNS, global clock, provisioning or merchant-data edit is allowed.

One worker, zero retries and a ten-minute deadline apply. Results, asset hashes,
policy/error observations, cleanup facts and selected product/cart/admin screenshots
are under ignored `artifacts/hosted/`. HTML reporting, tracing, video and automatic
failure screenshots stay off; owner sign-in failures omit credential-bearing errors.
Request routing enforces the browser write boundary and disables browser HTTP
caching; server cache headers and conditional 304 responses are checked separately.
The runner never starts/stops the API, rig or dependencies. Local HTTP E2E acceptance
does not certify deployment TLS/secure cookies, edge caching, real DNS/redirects,
multi-replica behavior or the live Packeta widget; those need a deployed check.

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
contains operator credentials. The shopper and merchant runners use only its API/rig addresses;
the operator suite below also reads the disposable operator sign-in.

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
services; routing blocks writes outside that boundary. Response-loss cases also forward
one allowed checkout or return POST, wait for its real successful answer, then abort
delivery to the browser. Forwarding uses a separate cookie jar so response cookies
are also lost; the checkout check verifies the browser retains its original cookies.
These cases never fabricate a success or retry the forwarded write.
No provider credentials,
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
The run owner's authenticated API session performs the required acceptance,
parcel receipt, whole-order refund and explicit retry of a Failed refund. Financial
actions carry fresh keys and are never automatically retried. This is fixture
progression, not merchant browser coverage. Owner credentials
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
Lost-write-answer cases verify the order or return committed while the browser still
shows uncertainty. Editing that draft blocks submission; restoring it permits an
explicit retry with the identical body and key. The API's `Idempotent-Replay` header,
account history, oracle and gateway prove one order/payment/return, one stock effect
and one invoice/credit note. Real payment and parcel-receipt progression complete the
recovered flows, including PDF downloads and captured mail. Exactly one failed browser
write transport is expected in each response-loss case; other failed writes fail checks.
This covers recovery on the same page with the retained attempt, not reload persistence.
Public refund checks use the additive contracts in backend `41d06f7`. Both guest and
account receipts show the recorded payment time, credited amount and public settlement
state separately. Account return history retains the saved listing/variant identities
and agrees with the order's refund facts. A guest sees a store-initiated whole-order
refund without needing an account return request. A refused provider refund remains
InProgress without a settlement date or private provider reason; after the owner
explicitly retries it, bounded receipt GET polling discovers Settled and its recorded
time. The original order total, one stock restoration, one credit note and one provider
refund are checked, with a real guest PDF download and no further browser business write.
These checks do not certify ByStore completion or uncertain provider refund outcomes.
Each test ends only its own run via `DELETE /runs/{run}` and verifies it is forgotten.
This suspends the tenant; its database rows remain until the stack is taken down.

The normal matrix has a ten-minute deadline, one worker and zero retries.
`npm run test:isolated:repeat` repeats every case twenty times, stops on the first
failure and has a 200-minute deadline (twenty ten-minute matrices). To focus a repeat,
append e.g. `-- --project=en-desktop --grep 'payment recovery'`.
Use `-- --project=en-desktop --grep 'lost (checkout|return) answer'` for the
post-commit response-loss cases.
Use `-- --project=en-desktop --grep 'store-initiated'` for the whole-order public
refund cases.
Every repetition creates its own run; a failed test is never retried. Reports, per-run
identities and order/stock/mail facts are under ignored `artifacts/isolated/`; use
`npx playwright show-report artifacts/isolated/report` to inspect them. Failure traces,
videos and screenshots are retained. Reports remain private because network records
can contain guest receipt tokens. Save reports before another invocation replaces them.

When finished, run `tests/e2e/stack.sh down` from the repository root to stop only the
disposable stack and discard its data. The separate merchant/operator suites, refund
uncertainty, scheduled recovery, complete repeat acceptance,
CI wiring and DNS remain separate work.
These journeys do not advance the stack-wide clock or run scheduled passes. Public
credited amounts remain distinct from provider settlement; settlement is asserted
through the current oracle and stand-in rather than inferred from the shopper label.

## Isolated merchant journeys

The merchant matrix uses the same disposable stack, with separately owned admin and
storefront previews. Start the stack as above, then run from this package:

```bash
npm run test:merchant
npm run test:merchant:repeat
```

The admin preview defaults to `127.0.0.1:5176`; set `SHOPFORGE_E2E_ADMIN_PORT` to a
distinct unprivileged port if necessary. The existing stack/storefront variables also
apply. Both previews use `SHOPFORGE_API_TARGET`. The runner verifies that both apps
reach the same E2E API/database marker before browser writes. It never starts or stops
the API/rig, and refuses an existing frontend server. The normal matrix has eight
cases across Czech/English and phone/desktop, one worker, zero retries and a ten-minute
deadline. The repeat command runs each case twenty times and stops on the first
failure; failed setup or development runs are not counted as repeat acceptance.

Every test gets a new rig run. The owner signs in through the admin UI. The setup
journey unpublishes one existing run host, creates and stocks a physical product,
lists it hidden, edits its price using a decimal comma, makes it visible, configures
Packeta address delivery and explicitly deactivates the saved manual shipping method.
It saves Test account metadata through the UI, verifies publication stays blocked
without a readable credential, saves a local-only credential separately, and publishes.
A shopper then buys that exact variant at the edited price. Duplicate Comgate pushes
must leave one paid attempt, one invoice and one stock deduction. The other store
host remains published and never gains the new listing. A separate journey refunds
a paid order through the merchant UI, then checks one credit note, one provider refund,
restored stock and the shopper's settled refund, including a further duplicate payment
notification and merchant reload.

The owner performs these operations through forms and buttons. Helper API calls are
reads, run setup/cleanup, or per-run provider controls; no helper API write stands in
for a merchant task. Reads of the real admin/public contracts verify saved settings;
the rig's database-backed stock and order readings verify inventory, exact variant and
unit price, payment attempts, documents and refunds. A narrow merchant write boundary
is separate from the unchanged shopper boundary. It allows only the run owner, its two
stores, known listing/method IDs, and product/variant/order IDs learned from successful
real responses. That forwarding records identities before delivering the original
response, with no retries, replacement payloads or fake success. Operator actions,
other runs, direct backend browser writes, Live account setup, Comgate secret replacement
and other financial actions remain blocked.

The Packeta reference/password in this matrix are deliberately local fixtures, used
only for the E2E environment's in-memory credential/readiness path. They do not define
Packeta's account-reference semantics, authenticate with Packeta, test its map, book
parcels or prove a live carrier sandbox. The rig already supplies the Comgate account;
its secret is never read or replaced by the journey. Merchant traces/video are disabled
so login and credential forms do not enter network bodies or DOM recordings. Browser
diagnostics record write paths and field names, not submitted values. Reports and
failure screenshots remain private under ignored `artifacts/merchant/`.

This tests merchandising/publication on the run's existing controlled `.localhost`
hosts. Creating a new store and proving a domain still need controlled DNS and the
backend-owned run/domain contracts. It does not certify deployment host restrictions,
CSP/caching, live providers, CI integration, or the separate shopper repeat matrix.

## Isolated operator journeys

With the same disposable stack running, use:

```bash
npm run test:operator
npm run test:operator:repeat
```

The operator suite owns both production previews, using the same stack descriptor and
admin/storefront port settings as the merchant suite. It verifies the E2E database/API
marker through both proxies before sign-in. Only this suite reads the descriptor's
stack-generated `operator@e2e.test` password; a deployment or development operator is
never accepted. Do not publish that descriptor. Keep the stack alive until the runner
has exited; the runner never starts or stops it.

Four CS/EN phone/desktop projects drive the real operator and merchant forms. Each
case owns a fresh rig run. The suspension case also creates a control run, whose
stores must keep serving throughout. Both target stores are warmed before the operator
confirms suspension. The next requests must return 404 on both hosts, the already-open
merchant shell must expire on its next protected read, and an authenticated customer's
profile must be denied. Resuming through the operator UI restores both hosts; the
merchant must sign in again, the customer's same profile becomes readable, and stock
and all other tenant statuses must remain unchanged. A separate case signs in/out of
both admin schemes on the same browser origin, proving their cookies cannot authorize
or sign out the other session. Confirmation/Escape, refreshed usage, direct reload,
mobile navigation and page reflow are checked. Two-factor enrollment/challenge tests
remain separate from this stack's password-only seeded operator.

Only UI forms/buttons perform operator and merchant auth and tenant status changes.
Helper requests are reads, rig run setup/cleanup and captured verification mail. A
separate operator boundary permits only the exact run tenant's bodyless suspend/resume,
its owner's sign-in/out, the isolated operator's sign-in/out, and customer registration/
verification on the run's first host. Other tenant/operator/plan/provisioning/financial
writes, direct backend browser requests and external origins are blocked. Responses
are never fabricated or retried. The operator list is global, so the test searches for
the checked run ID before opening its named confirmation; no other tenant can be changed.

Traces, video and automatic screenshots are disabled. The suite uses list/JUnit reports
because HTML report step titles retain filled passwords even without tracing. Failed
sign-in/verification closes that page before reporting a generic error, keeping credentials
and token-bearing page snapshots out of failure details. Explicit screenshots are taken
only after sign-in on tenant views. Diagnostics contain paths, field names and run facts,
never submitted values or cookie contents. Results stay private under `artifacts/operator/`.
The normal matrix has one worker, no retries and a ten-minute deadline; the repeat
command runs each case twenty times with fresh runs and stops on the first failure.
Each successful case cleans up only its own runs and verifies they were forgotten.
A failed run left suspended can also make rig cleanup refuse; keep the failure evidence
and discard the owned stack afterward, without an automatic status-change retry.

This proves local browser/API/database behavior on one API process. It does not certify
multi-replica invalidation timing, deployment host/CSP/cache rules, custom DNS/TLS, CI,
real providers or the independent shopper stability gate.
