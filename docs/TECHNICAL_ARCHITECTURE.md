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
| Application | Domain, Contracts, EF Core abstractions (no provider) | Infrastructure, Api, Desktop, Npgsql |
| Infrastructure | Application, Domain | Api, Desktop |
| Api | Application, Infrastructure, Contracts | Desktop |
| Desktop | Contracts | Domain, Application, Infrastructure, Api, EF Core, Npgsql |

These rules are enforced by `tests/DOF.ArchitectureTests`. The desktop client never connects to
PostgreSQL; it only calls the API.

**Data access decision (Phase 1):** Application services query through `IApplicationDbContext`
(EF Core `DbSet`s) rather than per-entity repositories. This keeps queries as efficient DTO
projections without an extra abstraction layer. Application references EF Core's provider-neutral
package only; Npgsql stays in Infrastructure.

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
  Ids are configured `ValueGeneratedNever`, so child entities added through a navigation (e.g. a
  role gaining a permission) are inserted rather than mistaken for existing rows.
- **Timestamps:** `DateTimeOffset` in the domain → `timestamptz` in PostgreSQL, always UTC.
  Date-only values use `DateOnly` → `date`.
- **Audit timestamps:** `AuditingInterceptor` sets `created_at` / `updated_at` on every save, so
  services never set them by hand. `ICurrentUser` now exists; `created_by` / `updated_by` columns
  will be added with the first tables the specification requires them on (projects, Phase 4).
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
| `Jwt:SigningKey` | HMAC-SHA256 key for access tokens (≥ 32 bytes) | **Secret.** Development: user-secrets. Production: `Jwt__SigningKey` env var / secret store. Validated at startup. |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenMinutes` (15), `Jwt:RefreshTokenDays` (14) | Token settings | `appsettings.json` |
| `Bootstrap:AdminEmail`, `Bootstrap:AdminPassword` | First Administrator, created only when no users exist | **Secret.** user-secrets / env vars |
| `RateLimiting:AuthPermitLimitPerMinute` (10) | Per-IP limit on login/refresh/logout/change-password | `appsettings.json` |
| `Serilog` | Log levels | `appsettings.json` |

A missing connection string or invalid timezone fails fast with a clear error.

## API conventions (established in Phase 0)

- Routes are prefixed `/api/v1`. Every endpoint requires authentication unless marked `[AllowAnonymous]`
  (health, system info, login, refresh, logout). Unknown routes therefore return 401 to anonymous callers.
- Successful responses use the `ApiResponse<T>` envelope (`success`, `data`, `message`) from Contracts.
- Errors (including 404s and unhandled exceptions) are returned as RFC 9457 ProblemDetails
  (`application/problem+json`).
- OpenAPI via Swashbuckle; Swagger UI at `/swagger`, exposed in Development only.
- Health: `/health` runs all checks including the database (readiness); `/health/live` runs none
  (liveness). The JSON response never includes exception details.
- Logging: Serilog to the console with per-request logging.

## Authentication and authorisation (Phase 1)

**Accounts.** The `users` table follows the specification (not ASP.NET Identity's full schema).
Passwords are hashed with ASP.NET Core Identity's `PasswordHasher` (PBKDF2, versioned, auto-rehash on
login). Emails are stored trimmed and lower-cased and are unique.

**Tokens.**
- *Access token:* JWT (HMAC-SHA256), 15 minutes, claims `sub`, `email`, `name`, `jti`. It carries no
  permissions.
- *Refresh token:* 512 random bits, 14 days. Only its SHA-256 hash is stored (`refresh_tokens`).
  Every refresh **rotates** it. All tokens from one sign-in share a `family_id`; presenting an
  already-rotated token revokes the whole family (theft detection).
- *Logout* revokes the refresh token. An access token already issued stays valid until it expires
  (≤ 15 minutes) unless the user is deactivated (below).
- *Password change* revokes all of the user's refresh tokens and returns a new pair.

**Per-request checks.** On every authenticated request, `JwtBearerEvents.OnTokenValidated` loads the
user's active flag and effective permissions from the database (one indexed query). Deactivated
users are rejected immediately, and permission changes apply on the next request. Permissions are
added as `permission` claims.

**Policies.** A fallback policy requires an authenticated user everywhere. Each permission in
`DOF.Domain.Rules.Permissions` is registered as a policy of the same name, used as
`[Authorize(Policy = Permissions.UsersManage)]`.

**Roles and permissions.** Permissions are a code-defined catalogue synchronised into the database at
startup (`DatabaseSeeder`). The four system roles get default grants only when the role or permission
is first created, so administrators' later changes via `PUT /roles/{id}/permissions` persist.
Guard rails: a user needs ≥ 1 role; you cannot deactivate yourself; the last active Administrator
cannot be deactivated or demoted; the Administrator role cannot lose `users.manage`.

**Errors.** `DofExceptionHandler` maps exceptions to ProblemDetails: not found → 404, duplicate or
stale version → 409, bad credentials → 401, business rule → 422, anything else → logged 500.
FluentValidation validators (in Application) run for every action argument through
`ValidationFilter` and return 400 `ValidationProblemDetails` with per-field errors.

**Rate limiting.** Fixed window per client IP on the authentication endpoints; 429 when exceeded.

**Desktop session.** `DOF.Desktop/Services/Api/AuthSession` signs in, keeps the access token in memory
only, refreshes it a minute before expiry, restores sessions on startup from the stored refresh token
and logs out (revoking server-side, best effort when offline). The refresh token is the only secret
persisted, through `ISecureStore`:
- macOS: `MacKeychainStore` (login keychain, generic password) — verified by an automated test.
- Windows: `WindowsCredentialStore` (Credential Manager, generic credential) — **not yet verified on
  Windows**; it compiles but has not run on a Windows machine.

Known limitations, accepted for now: no account lockout beyond rate limiting; no per-user permission
grants outside roles; access tokens are not revocable before expiry except through deactivation.

## Desktop client (Phase 2)

**Composition.** `Services/DesktopServices.Build` is the composition root (Microsoft.Extensions.DependencyInjection).
It is also used by the tests, which pass an in-memory server handler instead of the network.

**Talking to the API.** Three `HttpClient`s share one network handler:
- `AuthSession` (anonymous): sign-in, refresh, logout, change password.
- `ConnectionMonitor`: polls `GET /api/v1/system/info` every 15 s (8 s timeout).
- `ApiClient`: every authenticated call. `AuthenticatedHttpHandler` adds the access token
  (refreshing it first if it expires within a minute). `ApiClient` unwraps `ApiResponse<T>`, turns
  ProblemDetails into `ApiException` (user-readable message plus field errors), turns network failures
  into `ApiUnavailableException`, reports success/failure to the connection monitor, and raises
  `SessionExpired` on 401, after which the app signs out locally and shows the sign-in screen.

**Screens.** `MainWindowViewModel` shows a startup screen while it restores the stored session, then
either the sign-in screen (`LoginViewModel`) or the shell (`ShellViewModel`). If the server is
unreachable at startup, the stored session is kept and sign-in shows the offline state.

**Shell.** Collapsible sidebar (Dashboard, Calendar, Projects, Shows & Episodes, Tasks, Releases,
Reminders, Reports; Team, Settings and the user at the bottom). Top bar: page title, global search,
quick create, notifications, current date in the user's timezone, connection status (click to
re-check) and an account menu. Keyboard shortcuts: ⌘/Ctrl+1–8 sections, ⌘/Ctrl+B sidebar,
⌘/Ctrl+, settings. Search, quick create and notifications are visibly disabled until the phases that
give them data.

**Pages.** Each page derives from `PageViewModel` (title, `IsLoading`, `ErrorMessage`, Retry).
`RunAsync` turns expected API failures into a visible message; anything unexpected propagates to
the global handler. Live pages: Dashboard (greeting and date), Team (`GET /users` with search and
an include-deactivated filter) and Settings (profile, theme, change password, server). Sections
scheduled for later phases show a "coming in Phase N" page, never sample data.

**Views.** Compiled bindings everywhere. View models map to views through explicit `DataTemplate`s
in `App.axaml` (no reflection-based view locator). Colours are theme tokens in `Themes/Tokens.axaml`
with Light and Dark dictionaries; icons are original stroke geometries in `Themes/Icons.axaml`.

**Local state.** `PreferencesStore` keeps theme, sidebar state and last email in
`<LocalApplicationData>/DOFProductionManager/preferences.json` (non-sensitive only; a damaged file
falls back to defaults). The refresh token lives only in the OS secure store (Phase 1).

**Error handling and logs.** UI-thread exceptions, unobserved task exceptions and fatal errors are
written to `<LocalApplicationData>/DOFProductionManager/logs/desktop-YYYYMMDD.log` (14 days kept).
UI-thread errors are also shown in a dismissible banner. Nothing is swallowed silently.

## Testing

| Project | Scope |
|---|---|
| DOF.UnitTests | Pure logic, no I/O |
| DOF.IntegrationTests | The real API via `WebApplicationFactory`, against a real PostgreSQL database (`dof_production_manager_test`, override with `DOF_TEST_DATABASE`). The schema is dropped and rebuilt from migrations once per run; the host then seeds roles and a test Administrator. Tests run sequentially because some change shared role permissions. Also covers the desktop `AuthSession` against the real API, and the real macOS Keychain. EF Core's in-memory provider is not used. |
| DOF.ArchitectureTests | Layer dependency rules (above) |

**Desktop UI tests** (`DOF.IntegrationTests/Desktop/DesktopUiTests`) run the real views and view models
in a headless Avalonia window (Skia rendering) against the real API: sign-in, navigation to every
section, keyboard shortcuts, session restore, offline start, deactivation, password change and theme.
They save screenshots to `tests/DOF.IntegrationTests/bin/Debug/net10.0/ui-snapshots/` for visual
review. Avalonia's xUnit integration targets xUnit v3.2, so the tests drive `Avalonia.Headless`
directly. Note: they must call `HeadlessUnitTestSession.Dispatch` with a `Func<Task<T>>`; a plain
async lambda binds to `Dispatch<Task>` and would not be awaited.

Integration tests run in the `Testing` environment, so `appsettings.Development.json` is not loaded.

## Local environment

`deployment/scripts/setup-local-db.sh` creates the `dof_pm` role plus the `dof_production_manager_dev`
and `dof_production_manager_test` databases. These are fully separate from any other DOF database
on the machine.

Docker is not currently installed on the development machine. Container definitions in
`deployment/docker` will be added when deployment work begins.
