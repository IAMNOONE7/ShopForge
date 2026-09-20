# ShopForge

A multi-store e-commerce platform. One ASP.NET Core backend and one React storefront serve any number of independent stores, each with its own domain, branding, catalog and configuration — launching a new store is a matter of configuration, not a new deployment.

## Architecture

- **Modular monolith.** One ASP.NET Core host, one project per business module (`Stores`, `Access`, `Catalog`, `Orders`), module internals kept `internal`. Architecture tests enforce the dependency rules.
- **Multi-tenancy in one database.** Stores are resolved from the request hostname. EF Core query filters scope every store- and tenant-owned table, a save-time guard rejects cross-store writes, and composite foreign keys make cross-store links impossible in the database.
- **Shared products, per-store listings.** A physical product belongs to the company (tenant); each store lists it with its own name, price and visibility.
- **Metadata-driven product attributes.** Stores define typed attributes (numbers, dates, yes/no, options) stored as typed EAV rows. The storefront gets filters, ranges and counts computed from the data, so a furniture store and an electronics store run the same code with different filters.
- **Stores are created as data.** A tenant admin provisions a store with its address, currency, language and theme. It starts as a draft and goes live only once every module reports it ready (branding, products), with no deployment involved.
- **Guest checkout.** A server-side cart identified by a cookie, gross prices with a per-product VAT rate, and orders that snapshot the product name, price, VAT and the chosen payment and shipping method, numbered per store.
- **Bulk import.** Store staff upload an .xlsx file; rows are matched by SKU and update products, listings, categories and attribute values, with a per-row report of what changed and what was rejected.
- **Replaceable infrastructure behind small interfaces.** File storage (`IFileStorage`, Azure Blob Storage adapter, Azurite locally) and payments (`IPaymentProvider`, with methods the store settles itself today and a hosted provider later).

## Tech stack

- **Backend:** .NET 10, ASP.NET Core minimal APIs, Entity Framework Core, PostgreSQL
- **Frontend:** React, TypeScript, Vite — customer storefront and admin portal
- **Tooling:** Docker Compose, xUnit, Testcontainers, GitHub Actions

## Getting started

Prerequisites: .NET 10 SDK, Node.js 22.12+, Docker.

```bash
docker compose up -d                     # PostgreSQL on localhost:5433, Azurite on :10000
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

The API exposes `/health/live` and `/health/ready`.

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
    Access                    tenant users, sign-in, admin roles
    Catalog                   products, store listings, categories, images, attributes, import
    Orders                    carts, checkout, orders, payment and shipping methods
tests/
  ShopForge.UnitTests
  ShopForge.IntegrationTests
  ShopForge.ArchitectureTests module dependency rules
frontend/
  storefront                  customer-facing store
  admin                       store administration
```

## Status

Early development. Multi-store support, the product catalog, attribute-based filtering, bulk import, store provisioning and guest checkout are in place. Stock and reservations come next.
