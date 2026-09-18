# ShopForge

A multi-store e-commerce platform. One ASP.NET Core backend and one React storefront serve any number of independent stores, each with its own domain, branding, catalog and configuration — launching a new store is a matter of configuration, not a new deployment.

## Tech stack

- **Backend:** .NET 10, ASP.NET Core minimal APIs, Entity Framework Core, PostgreSQL
- **Frontend:** React, TypeScript, Vite — customer storefront and admin portal
- **Tooling:** Docker Compose, xUnit, Testcontainers, GitHub Actions

## Getting started

Prerequisites: .NET 10 SDK, Node.js 22.12+, Docker.

```bash
docker compose up -d                     # PostgreSQL on localhost:5433
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

Integration tests start their own PostgreSQL container, so Docker needs to be running.

## Repository layout

```
src/
  ShopForge.Api               ASP.NET Core host and composition root
  ShopForge.Infrastructure    persistence (single DbContext, migrations, tenancy filters)
  ShopForge.Shared            cross-cutting abstractions (current store context)
  Modules/
    Stores                    tenants, stores, domains, host-based store resolution
tests/
  ShopForge.UnitTests
  ShopForge.IntegrationTests
  ShopForge.ArchitectureTests module dependency rules
frontend/
  storefront                  customer-facing store
  admin                       store administration
```

## Status

Early development. Multi-store support is in place: stores are resolved from the request hostname and store-owned data is isolated. The product catalog comes next.
