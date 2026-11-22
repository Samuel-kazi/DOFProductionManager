# Technical Architecture

Living description of how the solution is actually built. The requirements live in
[TECHNICAL_SPECIFICATION.md](TECHNICAL_SPECIFICATION.md); this document records the decisions made
while implementing them.

## Layers and dependencies

```text
DOF.Desktop ──► DOF.Contracts ◄── DOF.Api ──► DOF.Application ──► DOF.Domain
                                     │              ▲
                                     └──► DOF.Infrastructure
```

| Project | May reference | Must not reference |
|---|---|---|
| Domain | nothing | any other layer, EF Core, ASP.NET Core |
| Contracts | nothing | Domain, Application, Infrastructure, EF Core |
| Application | Domain, Contracts | Infrastructure, Api, Desktop |
| Infrastructure | Application, Domain | Api, Desktop |
| Api | Application, Infrastructure, Contracts | Desktop |
| Desktop | Contracts | Domain, Application, Infrastructure, Api, EF Core, Npgsql |

These rules are enforced by `tests/DOF.ArchitectureTests`. The desktop client never connects to
PostgreSQL; it only calls the API.

## Build configuration

- `Directory.Build.props` sets `net10.0`, nullable reference types, implicit usings and
  **warnings as errors** for every project.
- `Directory.Packages.props` uses central package management. Every NuGet version is pinned
  there. EF Core / ASP.NET packages are deliberately held on the 10.0.x line (the 11.x releases
  target .NET 11, not the approved .NET 10 LTS).
- `global.json` pins the SDK (10.0.403, roll forward within the feature band) and opts into
  Microsoft.Testing.Platform, which `dotnet test` requires for xUnit v3 on the .NET 10 SDK.
- `dotnet-tools.json` pins `dotnet-ef` 10.0.15 as a local tool.

## Persistence conventions

Implemented in `DOF.Infrastructure/Persistence`:

- **Naming:** `EFCore.NamingConventions` maps everything to `snake_case` tables and columns.
- **Identifiers:** `Entity.Id` is a UUIDv7 (`Guid.CreateVersion7()`), time-ordered for index locality.
- **Timestamps:** `DateTimeOffset` in the domain → `timestamptz` in PostgreSQL, always UTC.
  Date-only values use `DateOnly` → `date`.
- **Audit timestamps:** `AuditingInterceptor` sets `created_at` / `updated_at` on every save, so
  services never set them by hand. (`created_by` / `updated_by` will be added in Phase 1 once a
  current-user abstraction exists.)
- **Optimistic concurrency:** entities implementing `IVersioned` get an integer `version` column
  configured as a concurrency token. The interceptor increments it on every update; a save made
  against a stale version raises `DbUpdateConcurrencyException`.
- **Archiving:** operational records implement `IArchivable` (`archived_at`) instead of being deleted.
- **Migrations:** live in `DOF.Infrastructure/Persistence/Migrations`. `Database:ApplyMigrationsOnStartup`
  is `true` only in `appsettings.Development.json`; production migrations are applied deliberately
  after review.

## Configuration

| Key | Purpose | Where set |
|---|---|---|
| `ConnectionStrings:Database` | PostgreSQL connection | Development: `appsettings.Development.json` (local trust auth, no secret). Production: environment variable `ConnectionStrings__Database` or a secret store, never committed. |
| `Database:ApplyMigrationsOnStartup` | Auto-apply migrations | Development only |
| `Organisation:DefaultTimeZone` | Display timezone default (`Africa/Nairobi`) | `appsettings.json`; validated at startup |
| `Serilog` | Log levels | `appsettings.json` |

A missing connection string or invalid timezone fails fast with a clear error.

## API conventions (established in Phase 0)

- Routes are prefixed `/api/v1`.
- Successful responses use the `ApiResponse<T>` envelope (`success`, `data`, `message`) from Contracts.
- Errors (including 404s and unhandled exceptions) are returned as RFC 9457 ProblemDetails
  (`application/problem+json`).
- OpenAPI via Swashbuckle; Swagger UI at `/swagger`, exposed in Development only.
- Health: `/health` runs all checks including the database (readiness); `/health/live` runs none
  (liveness). The JSON response never includes exception details.
- Logging: Serilog to the console with per-request logging.

## Testing

| Project | Scope |
|---|---|
| DOF.UnitTests | Pure logic, no I/O |
| DOF.IntegrationTests | The real API via `WebApplicationFactory`, against a real PostgreSQL database (`dof_production_manager_test`, override with `DOF_TEST_DATABASE`). Migrations are applied once per run via an xUnit assembly fixture. EF Core's in-memory provider is not used. |
| DOF.ArchitectureTests | Layer dependency rules (above) |

Integration tests run in the `Testing` environment, so `appsettings.Development.json` is not loaded.

## Local environment

`deployment/scripts/setup-local-db.sh` creates the `dof_pm` role plus the `dof_production_manager_dev`
and `dof_production_manager_test` databases. These are fully separate from any other DOF database
on the machine.

Docker is not currently installed on the development machine. Container definitions in
`deployment/docker` will be added when deployment work begins.
