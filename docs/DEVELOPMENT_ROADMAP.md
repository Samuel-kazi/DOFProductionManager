# Development Roadmap

Phases are defined in [TECHNICAL_SPECIFICATION.md](TECHNICAL_SPECIFICATION.md#part-10--development-phases).
A phase is complete only when it passes its acceptance criteria and the quality gates in Part 11.

| Phase | Scope | Status |
|---|---|---|
| 0 | Project foundation | ✅ Complete (2026-11-22) |
| 1 | Authentication and users | Not started |
| 2 | Desktop foundation | Not started |
| 3 | Dashboard and calendar | Not started |
| 4 | Tasks and projects | Not started |
| 5 | Shows and episodes | Not started |
| 6 | Release management | Not started |
| 7 | Reminder engine | Not started |
| 8 | Reports and refinement | Not started |

## Phase 0 — Project foundation ✅

| Acceptance criterion | Evidence |
|---|---|
| Solution builds | `dotnet build`: 0 warnings, 0 errors (warnings are errors) |
| API starts | `dotnet run --project src/DOF.Api` serves on `http://localhost:5028` |
| Database connects | `/health` reports the `database` check Healthy |
| Initial migrations apply | `InitialBaseline` applied to dev and test databases |
| Health endpoint works | `/health` and `/health/live` return 200; covered by integration tests |

Also delivered: central package pinning, Serilog, ProblemDetails errors, OpenAPI/Swagger, the
`ApiResponse<T>` envelope, the auditing/concurrency interceptor, organisation timezone
configuration, and 17 automated tests (unit, integration against PostgreSQL, architecture).

Deferred by design:
- `created_by` / `updated_by` population waits for the Phase 1 current-user abstraction.
- The desktop project is the stock Avalonia MVVM app, retitled; the real shell is Phase 2.
- Hangfire, SignalR and FluentValidation are added in the phases that first use them.
- Docker is not installed locally; container definitions come with deployment work.

## Supporting documents

`PRODUCT_REQUIREMENTS.md` and `UI_SPECIFICATION.md` are covered by the specification itself
(Parts 1 and 6) and will be split out if they start to diverge from it.
