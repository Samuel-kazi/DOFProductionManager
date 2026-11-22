# DOF Production Manager

Desktop application for coordinating the production operations of Dawn of Faith Ministry: projects, shows and episodes, releases across channels, tasks, reminders and reports, all visualised through one organisational calendar.

- **Desktop:** Avalonia UI (C#, MVVM), macOS and Windows
- **Backend:** ASP.NET Core Web API on .NET 10 LTS
- **Database:** PostgreSQL via Entity Framework Core + Npgsql

The governing document is [`docs/TECHNICAL_SPECIFICATION.md`](docs/TECHNICAL_SPECIFICATION.md). Current progress is tracked in [`docs/DEVELOPMENT_ROADMAP.md`](docs/DEVELOPMENT_ROADMAP.md).

## Prerequisites

- .NET SDK 10.0.403 or later 10.0 feature band (pinned in `global.json`)
- PostgreSQL 17 running locally
- Avalonia templates (only needed to scaffold new views): `dotnet new install Avalonia.Templates::12.1.6`

## Getting started

```bash
dotnet tool restore                      # restores the pinned dotnet-ef CLI
./deployment/scripts/setup-local-db.sh   # creates role dof_pm + dev/test databases
dotnet build
dotnet test                              # unit, integration (real PostgreSQL) and architecture tests
dotnet run --project src/DOF.Api         # http://localhost:5028/swagger
```

In Development the API applies pending migrations at startup. Elsewhere, apply them deliberately:

```bash
dotnet ef database update --project src/DOF.Infrastructure --startup-project src/DOF.Api
```

Useful endpoints: `GET /health` (readiness incl. database), `GET /health/live` (liveness), `GET /api/v1/system/info`.

## Repository layout

```text
src/DOF.Domain          entities and business rules (no dependencies)
src/DOF.Application     use cases and application services
src/DOF.Contracts       request/response DTOs shared by API and desktop
src/DOF.Infrastructure  EF Core, PostgreSQL, background jobs, external services
src/DOF.Api             ASP.NET Core Web API host
src/DOF.Desktop         Avalonia desktop client (talks to the API only)
tests/                  unit, integration and architecture tests
docs/                   specification, architecture and roadmap
deployment/             scripts and (later) container definitions
```
