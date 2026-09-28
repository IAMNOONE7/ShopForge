# ShopForge

A multi-store e-commerce platform. One ASP.NET Core backend and one React storefront serve any number of independent stores, each with its own domain, branding, catalog and configuration — launching a new store is a matter of configuration, not a new deployment.

## Architecture

- **Modular monolith.** One ASP.NET Core host, one project per business module (`Stores`, `Access`, `Catalog`, `Customers`, `Inventory`, `Orders`), module internals kept `internal`. Architecture tests enforce the dependency rules.
- **Multi-tenancy in one database.** Stores are resolved from the request hostname. EF Core query filters scope every store- and tenant-owned table, a save-time guard rejects cross-store writes, and composite foreign keys make cross-store links impossible in the database.
- **Shared products, per-store listings.** A physical product belongs to the company (tenant); each store lists it with its own name, price and visibility.
- **Metadata-driven product attributes.** Stores define typed attributes (numbers, dates, yes/no, options) stored as typed EAV rows. The storefront gets filters, ranges and counts computed from the data, so a furniture store and an electronics store run the same code with different filters.
- **Stores are created as data.** A tenant admin provisions a store with its address, currency, language and theme. It starts as a draft and goes live only once every module reports it ready (branding, products), with no deployment involved.
- **Guest checkout.** A server-side cart identified by a cookie, gross prices with a per-product VAT rate, and orders that snapshot the product name, price, VAT and the chosen payment and shipping method, numbered per store.
- **Shipping with pickup points and tracking.** Shipping methods name a provider the same way payment methods do. A method can require a pickup point, which the order then keeps; a paid order is shipped once and carries its tracking number to the shopper. The provider shipped today is the store itself — a carrier implements the same interface.
- **Card payments behind one interface.** Checkout sends the shopper to Stripe's hosted page, and the order is marked paid by the signed webhook, not by the browser coming back. Every event is recorded once, so retries change nothing. Without keys the provider is simply not offered and the store's own methods (bank transfer, pickup) stay.
- **Discount codes that keep the books straight.** Percentage, fixed amount or free shipping, with validity windows, a minimum order and usage limits. The discount is split across the cart's lines in proportion to what they cost, so every VAT rate keeps its share and the invoice summary still adds up; redemptions are counted atomically, so two shoppers cannot both take the last one.
- **Invoices that stand on their own.** Paying an order issues an invoice with the store's legal identity, its own per-store number series and a VAT summary per rate, copied at issue time rather than read back from the order. A refund goes through the provider that took the money, returns the goods to stock and issues a credit note. Both download as PDFs.
- **Reviews that come from orders.** Only a customer whose paid order contains the product can review it, once, and nothing reaches the shop until the store publishes it. The average and the number of reviews are kept on the listing as they change, so a page of products shows them and can sort by them without counting reviews per row. A signed-in customer also keeps a wishlist, which reads its products from the catalog, so anything that stops being sold drops out of it.
- **Returns that move money, goods and paperwork together.** A customer sends part of an order back within the store's return window; the store accepts it, and when the parcel arrives the returned lines are refunded with their share of the discount, the stock goes back and a credit note is issued for exactly what came back. The delivery is refunded only when nothing is left with the customer, and a store refunding an order outright is the same operation with everything outstanding in it.
- **Plans say what a company may have.** A plan is a set of caps — stores and products — that the platform assigns; the largest one caps nothing and is what a company is on until it is moved. A cap stops the next store or product being created and never removes what is already there, so moving a company down leaves its shops running, and an import that would cross the cap imports nothing rather than half a catalog.
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
- **Events through a transactional outbox.** What happens to an order is written with the change that caused it and delivered by a background worker: confirmations, payment receipts and shipment notices are handlers, not inline calls. Delivery is leased, retried with backoff, and anything that gives up lands in a failed-message list the store can requeue.
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

Card payments are off unless Stripe keys are configured:

```bash
export Payments__Stripe__SecretKey=sk_test_...
export Payments__Stripe__WebhookSecret=whsec_...
```

The API exposes `/health/live` and `/health/ready`.

## Deployment

`infra/` describes the whole Azure environment in Bicep: Log Analytics and Application Insights, Key Vault, Storage (product media plus the built frontends), PostgreSQL Flexible Server, the Container Apps environment and Front Door with its routes.

```bash
az deployment sub create --location westeurope --template-file infra/main.bicep --parameters infra/main.sample.bicepparam
```

The GitHub Actions `Deploy` workflow builds the image, pushes it, deploys the template, applies the migrations and uploads the built frontends. Three things are worth knowing:

- The application migrates the database only in Development. Everywhere else a migration bundle runs in the pipeline before the new revision starts serving.
- Secrets (database, storage, Stripe, Application Insights) live in Key Vault and are read by the container app's managed identity; the app is given only the vault's URI.
- Behind Front Door the original host arrives as a header, which is trusted only when `X-Azure-FDID` matches the configured profile — otherwise the forwarded headers are dropped.

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
frontend/
  storefront                  customer-facing store
  admin                       store administration
```

## Status

Early development. Multi-store support, the product catalog, attribute-based filtering, bulk import, store provisioning, guest checkout, stock reservations, customer accounts, card payments, shipping, background processing, telemetry, an Azure deployment (written, not yet run), invoicing, discount codes, reviews, wishlists, returns, platform administration, plan caps, staff invitations, password management, customer self-service, an audit trail, data export and erasure, retention sweeps and consent records are in place.
