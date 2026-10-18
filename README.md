# ShopForge

A multi-store e-commerce platform. One ASP.NET Core backend and one React storefront serve any number of independent stores, each with its own domain, branding, catalog and configuration — launching a new store is a matter of configuration, not a new deployment.

## Architecture

- **Modular monolith.** One ASP.NET Core host, one project per business module (`Stores`, `Access`, `Catalog`, `Customers`, `Inventory`, `Orders`), module internals kept `internal`. Architecture tests enforce the dependency rules.
- **Multi-tenancy in one database.** Stores are resolved from the request hostname. EF Core query filters scope every store- and tenant-owned table, a save-time guard rejects cross-store writes, and composite foreign keys make cross-store links impossible in the database.
- **Shared products, per-store listings.** A physical product belongs to the company (tenant); each store lists it with its own name, price and visibility.
- **Metadata-driven product attributes.** Stores define typed attributes (numbers, dates, yes/no, options) stored as typed EAV rows. The storefront gets filters, ranges and counts computed from the data, so a furniture store and an electronics store run the same code with different filters.
- **A merchant brings their own domain.** A store answers on the subdomain the platform gave it and on any name the merchant has proved they own, by publishing a TXT record the admin shows them. Nothing is served until that proof is in DNS, so no shop can answer on a name belonging to somebody else, and a name is taken across the whole platform the moment it is asked for. One domain is the primary — what mail and documents link to — and it cannot be an unproved one, or removed while it holds the job.
- **Stores are created as data.** A tenant admin provisions a store with its address, currency, language and theme. It starts as a draft and goes live only once every module reports it ready (branding, products), with no deployment involved.
- **Guest checkout.** A server-side cart identified by a cookie, gross prices with a per-product VAT rate, and orders that snapshot the product name, price, VAT and the chosen payment and shipping method, numbered per store.
- **Shipping with pickup points and tracking.** Shipping methods name a provider the same way payment methods do. A method can require a pickup point, which the order then keeps; a paid order is shipped once and carries its tracking number to the shopper. The provider shipped today is the store itself — a carrier implements the same interface.
- **Card payments behind one interface.** Checkout sends the shopper to Stripe's hosted page, and the order is marked paid by the signed webhook, not by the browser coming back. Every event is recorded once, so retries change nothing. Without keys the provider is simply not offered and the store's own methods (bank transfer, pickup) stay.
- **Discount codes that keep the books straight.** Percentage, fixed amount or free shipping, with validity windows, a minimum order and usage limits. The discount is split across the cart's lines in proportion to what they cost, so every VAT rate keeps its share and the invoice summary still adds up; redemptions are counted atomically, so two shoppers cannot both take the last one.
- **Invoices that stand on their own.** Paying an order issues an invoice with the store's legal identity, its own per-store number series and a VAT summary per rate, copied at issue time rather than read back from the order. A refund goes through the provider that took the money, returns the goods to stock and issues a credit note. Both download as PDFs.
- **Reviews that come from orders.** Only a customer whose paid order contains the product can review it, once, and nothing reaches the shop until the store publishes it. The average and the number of reviews are kept on the listing as they change, so a page of products shows them and can sort by them without counting reviews per row. A signed-in customer also keeps a wishlist, which reads its products from the catalog, so anything that stops being sold drops out of it.
- **Returns that move money, goods and paperwork together.** A customer sends part of an order back within the store's return window; the store accepts it, and when the parcel arrives the returned lines are refunded with their share of the discount, the stock goes back and a credit note is issued for exactly what came back. The delivery is refunded only when nothing is left with the customer, and a store refunding an order outright is the same operation with everything outstanding in it.
- **Plans say what a company may have.** A plan is a set of caps — stores and products — that the platform assigns; the largest one caps nothing and is what a company is on until it is moved. A cap stops the next store or product being created and never removes what is already there, so moving a company down leaves its shops running, and an import that would cross the cap imports nothing rather than half a catalog.
- **A second factor for the people who run a shop, and for the people who run the platform.** Staff and platform operators can turn on an authenticator app: the codes are the ordinary six-digit ones, enrolment takes two steps so nobody locks themselves out by closing the tab, and ten single-use recovery codes are handed over once. Signing in then takes the password and the code, with a five-minute ticket between them that stops working if the password changes in between — and a ticket earned in one of the two sign-in domains is unreadable in the other.
- **A request sent twice does one thing.** Checkout, asking to send something back, receiving the parcel and a store's refund all take an `Idempotency-Key`: the first request does the work and a repeat of it is answered with exactly what the first one said, so a shopper whose connection drops can press the button again without placing a second order. A key used for a different request is refused rather than answered with somebody else's order, and a request that was refused does not spend its key.
- **Passwords nobody has to pass on.** Everybody who works here — a company's staff and the platform's operators alike — changes their own password, resets a forgotten one by e-mail, and can sign out everywhere at once. A stamp in the cookie is compared with the person's current one on every request, so a password change, a reset or a withdrawal of access ends every other session within the next request rather than whenever the cookie expires.
- **Colleagues are invited, not handed a password.** A company sees who works on it and brings somebody in by e-mail: the invitation carries the role, works once, and is where the new colleague chooses their own password. Roles change and people are switched off rather than deleted, because orders and invoices name them. An owner is only ever created or changed by an owner, and nobody changes their own role, so a company cannot lock itself out.
- **The platform itself has an operator.** A third kind of user, outside any company: it signs in with its own cookie, sees every tenant with what it uses (stores, products, orders, customers), takes a new one on by inviting its first owner — the operator never knows anybody's password — and suspends one when it has to. Operators bring in other operators the same way. Suspension closes that company's shops and its admin within the request, keeps every row, and is undone by resuming it.
- **Customer accounts, per store.** One authentication identity inside a company, one relationship per store: an account created in one store cannot sign in to another, and each store sees only its own customers and their orders. Registration confirms the address by e-mail, a lost confirmation can be asked for again, and orders placed as a guest are handed over once that address is proven. Moving to a new address needs the password and a link sent to the new address, and moves that store's account alone — the same person's account at another store of the company keeps the address it had.
- **Stock that cannot oversell.** Warehouses hold the tenant's stock, shared by its stores. Checkout reserves with a conditional update, so two shoppers racing for the last item cannot both get it; paying turns the reservation into a stock movement and an unpaid order releases it when it expires.
- **Bulk import.** Store staff upload an .xlsx file; rows are matched by SKU and update products, listings, categories and attribute values, with a per-row report of what changed and what was rejected.
- **A person can take their data and have it removed.** A customer downloads everything one store holds about them — account, orders, returns, invoices, reviews, wishlist — and can have it erased with their password. Erasing is not deleting everywhere: an order and its invoices keep their numbers, lines, totals and VAT, because that is what the shop shows the tax authority, and lose the name, address and e-mail. Everything else goes, including the address itself once no other store of the company is still using it.
- **Consent is asked for and kept.** A customer says yes or no to being written to about anything beyond their own orders, and the answer is stored with the words they were shown, when they gave it and from where, so it can be shown later. They can change their mind, it is in their export, and it goes when they do. Nothing sends on it yet — that is what it will be checked against when it does.
- **Nothing personal is kept longer than it earns.** A daily sweep clears abandoned carts and unconfirmed sign-ups inside each store, and, once a day with nothing in scope, delivered outbox messages and spent staff and operator links after a month and audit entries after a year. A message that gave up is left where somebody can see it — a dead letter is waiting for a person, not for a sweep.
- **A record of what was done.** Money moving, access changing and prices changing all leave an append-only entry naming who did it, from where, and to which company or store — written where the decision is made, in the same transaction, so a change that rolls back leaves no claim that it happened. A company reads its own entries and no others; the platform reads a company's, or its own. Nothing can edit or delete one: the save itself refuses.
- **An address that bounces stops being written to.** A signed webhook from the provider files a permanent failure or a spam complaint against the store whose message it was — the store the message carried with it, since an address alone does not say whose customer it is. That store sees the addresses it may not write to and can let one back in; nobody else sees them. A message to a suppressed address is finished rather than failed: nothing went wrong, we chose not to send it.
- **Documents arrive with the mail that mentions them.** A payment receipt carries its invoice and a refund notice its credit note, as real PDFs. The queued message only names the document; the bytes are fetched when the mail is sent, which keeps PDFs out of the queue and means the invoice is reliably there by then — it is issued by a handler of the same event that asks for the receipt. A document that cannot be found is logged and the message goes without it, because a bare receipt beats no receipt.
- **Mail that looks like the store that sent it.** Every message goes out in the store's colour with its logo and name, in HTML with the plain text alongside, and money written the way that store's customers read it. The look is applied once on the way out, so a handler writes words and nothing else; mail that belongs to no store goes out as the platform.
- **Mail through a provider, or none at all.** Transactional mail is handed to a provider (Mailgun today) behind one interface, chosen by configuration; with nothing configured it goes to the log, which is how development runs. A shop's mail is sent under the shop's name and the platform's verified address. A provider that is down or refuses a message fails loudly, so the outbox in front of it backs off, retries, and eventually puts the message where somebody can see it.
- **Events through a transactional outbox.** What happens to an order is written with the change that caused it and delivered by a background worker: confirmations, payment receipts and shipment notices are handlers, not inline calls. Delivery is leased, retried with backoff, and anything that gives up lands in a failed-message list the store can requeue.
- **Limits nothing can slip past.** Three windows — signing in, changing something, and work that costs real time — applied to whole route groups, with a ceiling under all of them so an endpoint nobody remembered to limit is limited anyway. Counting is per account where somebody is signed in and per address otherwise. Request bodies are refused by the server rather than read and then judged, and every response carries headers telling a browser to do nothing with it: this API answers JSON and files, never a page.
- **Telemetry that follows the work.** OpenTelemetry traces cover a request, its database calls and the background work it causes — an order and the confirmation e-mail sent seconds later share one trace — plus business counters (orders, payments, refused reservations) and outbox gauges. Nothing is exported unless an OTLP endpoint is configured.
- **Replaceable infrastructure behind small interfaces.** File storage (`IFileStorage`, Azure Blob Storage adapter, Azurite locally) and payments (`IPaymentProvider`, with methods the store settles itself today and a hosted provider later).

## Tech stack

- **Backend:** .NET 10, ASP.NET Core minimal APIs, Entity Framework Core, PostgreSQL
- **Frontend:** React, TypeScript, Vite — customer storefront and admin portal
- **Tooling:** Docker Compose, xUnit, Testcontainers, GitHub Actions

## Getting started

Prerequisites: .NET 10 SDK, Node.js 22.12+, Docker.

```bash
docker compose up -d                     # PostgreSQL on :5433, Azurite on :10000, OTLP collector on :4317
dotnet run --project src/ShopForge.Api   # API on http://localhost:5080
```

Frontends, each in its own terminal:

```bash
cd frontend/storefront && npm install && npm run dev   # http://localhost:5173
cd frontend/admin && npm install && npm run dev        # http://localhost:5174
```

In development the API applies database migrations on startup and seeds two demo stores, so the same storefront shows a different store per hostname:

- http://shop-a.localhost:5173 — Wooden Home
- http://shop-b.localhost:5173 — Volt Electronics

The admin app (http://localhost:5174) signs in with the development account `owner@demo.local` / `ShopForge-demo-1`.
Administering the platform itself has no UI yet; its API signs in at `/api/platform/auth/login` with
`platform@demo.local` / `ShopForge-platform-1`.

Telemetry is off unless an OTLP endpoint is configured. Compose brings a collector that prints what it receives:

```bash
export OpenTelemetry__Otlp__Endpoint=http://localhost:4317
docker compose logs -f otel-collector
```

E-mail goes to the log until a provider is configured. Another provider is one class behind `IEmailDelivery` and
one setting:

```bash
export Email__Provider=mailgun
export Email__SenderAddress=no-reply@mg.example.com
export Email__Mailgun__ApiKey=...
export Email__Mailgun__Domain=mg.example.com
export Email__Mailgun__BaseUrl=https://api.mailgun.net   # the default is the European host
export Email__Mailgun__WebhookSigningKey=...            # without it, bounce events are refused
```

Asking for a provider without its settings stops the application at startup rather than losing mail quietly.

Stripe is the deployment's own account, not a store's: payments through it go to whoever holds the keys. The keys register it, and stores may choose it only where every store on the deployment is the key holder's:

```bash
export Payments__Stripe__SecretKey=sk_test_...
export Payments__Stripe__WebhookSecret=whsec_...
export Payments__Stripe__OfferToStores=true   # only when every store here is yours
```

The API exposes `/health/live` and `/health/ready`.

## Deployment

`infra/` describes the whole Azure environment in Bicep: Log Analytics and Application Insights, the app's managed identity and its roles, Key Vault, Storage (product media), PostgreSQL Flexible Server, the Container Apps environment with the app and its migration job, and Front Door with its route, caching rule, WAF policy and the admin's own domain. The vaults, the database and Front Door send their logs to the environment's workspace.

Each environment is `infra/main.bicep` with its own parameter file in `infra/environments/`. The file holds only the environment's name and sizes. Host names, mail, the image and every secret come from the GitHub environment of the same name when a release runs. `infra/bootstrap.bicep` creates what the environments share: the container registry, and the identity the `Deploy` workflow signs in as. `tests/ShopForge.DeploymentTests` compiles and lints all of it on every build, with the Bicep compiler as a package.

The GitHub Actions `Deploy` workflow runs a release in this order:

1. `scripts/release-inputs.sh` checks every input by name, and refuses a release that would send no mail.
2. The image, which builds and carries both frontends, is built, stamped with its commit, and pushed.
3. `scripts/provision.sh <environment> without-app` deploys everything the app needs, including the migration job on the new image. The serving app is left alone.
4. `scripts/migrate.sh <environment>` runs that job: the release's own image, run with `migrate`, applies its migrations from inside the environment.
5. `scripts/provision.sh <environment> with-app` puts the image on the app.
6. `scripts/release-smoke.sh <environment> <commit> api` checks the API through Front Door, including that the answering build is the release's own (`/health/version`).
7. `scripts/release-smoke.sh <environment> <commit> site` checks what a shopper and a merchant open, and that the app refuses a request that went around Front Door.

Every address the smoke tests use comes from the release's own deployment, so a new environment is checked before it has a domain or a shop.

### Creating an environment

1. Once per subscription, somebody allowed to assign roles deploys the bootstrap:

   ```bash
   az deployment sub create --name shopforge-bootstrap --location westeurope \
     --template-file infra/bootstrap.bicep \
     --parameters registryName=<registry name> repository=<owner>/<repository>
   ```

   The identity it creates signs in from this repository's `staging` and `production` GitHub environments only, with no secret. It can manage resources, and it can grant only the three roles a release grants its app: AcrPull and the two Key Vault secrets roles.

2. Create the GitHub environment (`staging` or `production`). Limit its deployment branches to `main`, and give production a required reviewer: a job in that environment is what the deployer trusts.
   - **Secrets:** `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID` from the bootstrap's outputs; `DATABASE_PASSWORD`, `MAILGUN_API_KEY` and `MAILGUN_WEBHOOK_SIGNING_KEY`; and `STRIPE_SECRET_KEY` and `STRIPE_WEBHOOK_SECRET` if the deployment takes cards through Stripe.
   - **Variables:** `LOCATION`, plus `REGISTRY_NAME`, `REGISTRY_SERVER` and `REGISTRY_RESOURCE_GROUP` from the bootstrap's outputs; `STORE_HOST_SUFFIX`; `EMAIL_PROVIDER` (`mailgun`), `EMAIL_SENDER_ADDRESS` and `MAILGUN_DOMAIN`; and `FIRST_OPERATOR_EMAIL`, the person who will run the platform.

3. Run `Deploy` for that environment. The admin is served on Front Door's own host name (the deployment's `publicHostName` output), at `/admin/`, and the smoke tests check everything there.

4. The release's migration job has invited `FIRST_OPERATOR_EMAIL`, and the invitation arrives by mail. Until the admin has a page for it, accept it with the token from the link and a password of your own: `POST https://<publicHostName>/api/platform/invitations/accept` with `{ "token": "…", "password": "…" }`. Until somebody accepts, every release sends a fresh invitation in place of the last, and so does starting the migration job again: `az containerapp job start --name <migrationJob> --resource-group <resourceGroup>`, both deployment outputs. Once the platform has an operator, the variable does nothing; operators invite each other.

5. To give the admin its own domain, set `ADMIN_HOST` and run `Deploy`. Then add a CNAME from the admin host to the `publicHostName` output, and a TXT record at `_dnsauth.<admin host>` holding the `adminValidationToken` output. Front Door validates the domain and issues its certificate. The admin stays on Front Door's own host name too, so nothing waits for the records.

6. Once a store is published, set `SMOKE_STORE_HOST` to its host, and the smoke tests check it too.

A few things are worth knowing:

- The application migrates the database only in Development. A build whose database lacks any of its migrations is not ready: it answers every request except `/health/*` with a 503, and does no background work until the migrations are in.
- Outside Development and the test environments, the application refuses to start if its mail would only be logged.
- Secrets (database, storage, Mailgun, Stripe, Application Insights) live in Key Vault, read by the app's own managed identity; the app is given only the vault's URI.
- The app serves both built frontends: the storefront's page for every storefront route, and the admin's under `/admin`, on the platform's own hosts only: the admin's domain and Front Door's own host name. Each page has its own content security policy. Hashed assets are cached for a year, pages not at all, and nothing under `/api` turns into a page. `robots.txt` and the sitemap answer at each shop's root.
- Outside development the app answers only Front Door: a request without the configured profile's `X-Azure-FDID` is refused, health probes apart. The original host arrives as a forwarded header, trusted only on a request that carries the profile id.
- The template owns only the platform's own route, security policy and domains. Merchants' domains belong to a route and a security policy of their own, which a redeploy never touches.

`tests/release/rehearse.sh` rehearses a release on this machine. It runs the previous build and the working tree's across a real migration, the rollback included, against a throwaway PostgreSQL and Azurite.

## Database migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/ShopForge.Infrastructure --startup-project src/ShopForge.Api --output-dir Persistence/Migrations
```

## Tests

```bash
dotnet test
```

Integration tests start their own PostgreSQL and Azurite containers, so Docker needs to be running.

### The end-to-end stack

Browser journeys run against a stack of their own: a throwaway PostgreSQL and Azurite in compose, the API in its `E2E`
environment on `127.0.0.1:5180`, and a test rig on `127.0.0.1:5190` that stands in for Comgate and Mailgun. The `E2E`
environment refuses to start pointed at anything off this machine.

```bash
tests/e2e/stack.sh up      # addresses in .e2e/stack.json
curl -X POST http://127.0.0.1:5190/runs
dotnet test --project tests/ShopForge.EndToEndTests
tests/e2e/stack.sh down    # and the data with it
```

A run is a company of its own with two published stores on `<run>-a.localhost` and `<run>-b.localhost`, a T-shirt in
three sizes and a mug. The rig answers for the run under `/runs/{run}`:

| | |
|---|---|
| `GET /runs/{run}` | the run: stores, hosts, products, variants, the owner's sign-in |
| `PUT /runs/{run}/comgate/behaviour` | `push` (`deliver`, `drop`, `twice`), `pushDelayMs`, `statusDelayMs`, `statusFails`, `refund` (`accept`, `refuse`, `fail`), `createFails` |
| `POST /runs/{run}/comgate/transactions/{id}` | `{ "status": "PAID", "push": true }`; any status, including one Comgate does not have |
| `GET /runs/{run}/mail?to=` | letters to the run's addresses, with their links |
| `GET /runs/{run}/stores/{store}/orders/{number}` | the order, its attempts, answers, kept money, returns, documents and letters |
| `GET /runs/{run}/stock/{sku}` | on hand, reserved, available |
| `POST /runs/{run}/clock` | `{ "advanceMinutes": 40 }`: the stack's clock, for every run on it |
| `POST /runs/{run}/passes/{name}` | `expired-orders`, `store-catch-up` or `store-maintenance`, now |

A checkout with Comgate sends the browser to the rig's payment page, whose Pay, Decline and Leave pending buttons send
it back to the store.

## Repository layout

```
src/
  ShopForge.Api               ASP.NET Core host and composition root
  ShopForge.Infrastructure    persistence (single DbContext, migrations, tenancy filters), blob storage
  ShopForge.Shared            cross-cutting abstractions (store context, file storage, claims)
  Modules/
    Stores                    tenants, stores, domains, store resolution, provisioning, logos
    Access                    tenant users, sign-in, admin roles, invitations, passwords
    Catalog                   products, store listings, categories, images, attributes, import
    Customers                 customer identities, store customers, sign-in, verification, password reset, address changes
    Inventory                 warehouses, stock levels, movements, reservations
    Orders                    carts, checkout, orders, payment and shipping methods, payment webhooks
    Platform                  platform operators: the users who administer ShopForge itself
tests/
  ShopForge.UnitTests
  ShopForge.IntegrationTests
  ShopForge.ArchitectureTests module dependency rules
  ShopForge.TestRig           stand-ins for Comgate and Mailgun, runs and read-only readings for the end-to-end stack
  ShopForge.EndToEndTests     the harness checked against a running end-to-end stack
  ShopForge.DeploymentTests   the templates compiled and linted, the release's scripts run against stand-ins
  e2e/                        the end-to-end stack's compose file and script
  release/                    a release rehearsed on this machine, across a real migration
frontend/
  storefront                  customer-facing store
  admin                       store administration
infra/
  bootstrap.bicep             what every environment shares: the registry and the identity releases deploy as
  main.bicep, modules/        one environment
  environments/               staging and production, a name and sizes each
scripts/                      a release's steps and its smoke tests
```

## Status

Early development. Multi-store support, the product catalog, attribute-based filtering, bulk import, store provisioning, guest checkout, stock reservations, customer accounts, card payments, shipping, background processing, telemetry, an Azure deployment (written, not yet run), invoicing, discount codes, reviews, wishlists, returns, platform administration, plan caps, staff invitations, password management, two-factor sign-in, idempotent public writes, customer self-service, an audit trail, data export and erasure, retention sweeps, consent records and transactional e-mail with a real provider are in place.
