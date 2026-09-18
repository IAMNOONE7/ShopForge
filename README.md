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

The API exposes `/health/live` and `/health/ready`.

## Tests

```bash
dotnet test
```

Integration tests start their own PostgreSQL container, so Docker needs to be running.

## Repository layout

```
src/
  ShopForge.Api               ASP.NET Core host
  ShopForge.Infrastructure    persistence and other technical concerns
tests/
  ShopForge.IntegrationTests
frontend/
  storefront                  customer-facing store
  admin                       store administration
```

## Status

Early development. The technical foundation is in place; multi-store support comes next.
