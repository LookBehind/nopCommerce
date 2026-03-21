# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build the entire solution
dotnet build src/NopCommerce.sln

# Build and run the web application (port 4000)
dotnet build src/NopCommerce.sln && dotnet run --no-build --project src/Presentation/Nop.Web/Nop.Web.csproj

# Run all tests
dotnet test src/NopCommerce.sln

# Run a specific test project
dotnet test src/Tests/Nop.Core.Tests/Nop.Core.Tests.csproj
dotnet test src/Tests/Nop.Services.Tests/Nop.Services.Tests.csproj
dotnet test src/Tests/Nop.Web.MVC.Tests/Nop.Web.MVC.Tests.csproj

# Run a specific test by name
dotnet test src/Tests/Nop.Services.Tests/Nop.Services.Tests.csproj --filter "FullyQualifiedName~TestMethodName"
```

No hot-reload — a full rebuild is required after any code change.

## Architecture

This is a **nopCommerce 4.4** fork customized for the **MySnacks** platform, running on **.NET 7** (SDK 7.0.306).

### Layered Structure

```
Presentation/
├── Nop.Web              → ASP.NET Core MVC app (public store + Admin area)
└── Nop.Web.Framework    → Web infrastructure (routing, validation, plugin loading, middleware)

Libraries/
├── Nop.Core             → Domain entities, caching, events, configuration
├── Nop.Data             → Data access via LINQ2DB (not EF Core), FluentMigrator migrations
└── Nop.Services         → Business logic layer (41 service domains)

Plugins/                 → 26+ modular plugins (payments, shipping, tax, widgets, etc.)

Tests/
├── Nop.Tests            → Shared test infrastructure (BaseNopTest, SQLite in-memory DB)
├── Nop.Core.Tests       → Core entity tests
├── Nop.Services.Tests   → Service layer tests
└── Nop.Web.MVC.Tests    → Presentation tests
```

### Key Architectural Decisions

- **ORM**: Uses **LINQ2DB** (not Entity Framework). Data providers in `Nop.Data` support MS SQL Server, MySQL, and PostgreSQL.
- **DI Container**: **Autofac** (not the default ASP.NET Core DI). Service registration happens via `DependencyRegistrar` classes.
- **All methods are async** — follow this pattern consistently.
- **Repository pattern**: `IRepository<T>` in `Nop.Data` for all data access.
- **Database migrations**: FluentMigrator in `Nop.Data/Migrations/`.
- **Authentication**: JWT Bearer tokens configured in `appsettings.json`. APIs use Swagger/OpenAPI.

### Plugin System

Plugins live in `src/Plugins/` and each has a `plugin.json` manifest. They implement `IPlugin` from `Nop.Services/Plugins/`. Plugin DLLs are output to `Presentation/Nop.Web/Plugins/{PluginName}/`. A post-build target (`ClearPluginAssemblies`) cleans unnecessary assemblies from plugin output directories.

Custom MySnacks plugins: `BuyAmScraper`, `Notifications.Manager`, `Company.Company`, `Payments.Idram`.

### Test Infrastructure

Tests use **NUnit** with **Moq** and **FluentAssertions**. `BaseNopTest` in `Nop.Tests` bootstraps the full DI container with an **SQLite in-memory database**, so tests run against real service implementations rather than mocks.

## Code Style

Enforced via `.editorconfig`:
- 4-space indent for C#, 2-space for XML/JSON/JS/CSS
- Private fields: `_camelCase` prefix
- Constants: `ALL_UPPER`
- Interfaces: `I` prefix (PascalCase)
- Use `var` when type is apparent
- Braces on new lines (Allman style)
- No `this.` qualification

## Deployment

- Docker multi-stage build (`Dockerfile` at repo root) targeting `aspnet:7.0-alpine`
- Azure DevOps pipeline (`azure-pipelines.yml`) builds Docker image and updates Kubernetes manifests
- Branches triggering CI: `staging`, `old-4.30`, `master`
