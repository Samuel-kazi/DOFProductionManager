# Development Roadmap

Phases are defined in [TECHNICAL_SPECIFICATION.md](TECHNICAL_SPECIFICATION.md#part-10--development-phases).
A phase is complete only when it passes its acceptance criteria and the quality gates in Part 11.

| Phase | Scope | Status |
|---|---|---|
| 0 | Project foundation | ✅ Complete (2026-11-22) |
| 1 | Authentication and users | ✅ Complete (2026-11-22) |
| 2 | Desktop foundation | ✅ Complete (2026-11-22) |
| 3 | Dashboard and calendar | ✅ Complete (2026-11-22) |
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

## Phase 1 — Authentication and users ✅

| Acceptance criterion | Evidence |
|---|---|
| Users can log in | `AuthTests.Valid_credentials_return_tokens_and_the_profile`; manual check against the dev API |
| Invalid credentials are rejected | Wrong password, unknown email, deactivated user and empty request all tested (401 / 400) |
| Permissions are enforced by the API | Anonymous → 401, missing permission → 403, granted → allowed; permission changes take effect without re-login |
| Users can securely log out | Logout revokes the refresh token server-side; desktop `AuthSession` clears the OS secure store |

Also delivered: refresh-token rotation with reuse detection, password change, user CRUD with
optimistic concurrency, role-permission management, Administrator lockout guards, per-IP rate
limiting, FluentValidation, ProblemDetails error mapping, Swagger bearer auth, macOS Keychain and
Windows Credential Manager stores. 59 automated tests pass.

Deferred / open:
- `GET /users/{id}/workload` waits for tasks (Phase 4).
- `WindowsCredentialStore` is untested on Windows; verify when a Windows machine is available.
- `activity_logs` (audit trail) arrives with the first operational records (Phase 4). Phase 1
  security events (logins, permission changes, deactivations) are written to the structured log.
- The desktop login screen itself is Phase 2; Phase 1 delivers the session service it will use.

## Phase 2 — Desktop foundation ✅

| Acceptance criterion | Evidence |
|---|---|
| Desktop app launches on macOS | Launched from the build on macOS 27.3 (arm64) against the local API; the log shows startup and "Online" |
| Desktop app connects to the API | Connection indicator driven by `/api/v1/system/info` and every API call; offline start tested |
| Login works | Headless UI test signs in through the real `LoginView`; wrong password and unreachable server show clear messages; stored sessions restore on restart |
| Navigation works | UI test visits every sidebar section; shortcuts tested (⌘/Ctrl+3, ⌘/Ctrl+B, ⌘/Ctrl+,) |
| Connection state is visible | Top bar and sign-in screen show Connected / Connecting… / Offline; offline banner in the shell |

Also delivered: light/dark/system theme saved between launches, collapsible sidebar, Team directory,
Settings (profile, theme, change password), session-expiry handling, file logging, error banner,
and headless UI tests that save screenshots. 80 automated tests pass.

Deferred / open:
- Global search, quick create and notifications are visible but disabled until Phases 3 and 7.
- Native desktop notifications and auto-update are separate packaging tasks (spec 2.1); not started.
- App icon is still the Avalonia placeholder; needs a DOF icon asset.
- Windows: not yet run (no Windows machine available).

## Phase 3 — Dashboard and calendar ✅

| Acceptance criterion | Evidence |
|---|---|
| Events appear in the correct calendar views | API tests for range overlap and day/week/month/agenda boundaries in Nairobi time; UI test checks month cells, week/day minutes and lanes, and agenda grouping |
| Source records are represented without duplication | Calendar assembled from `ICalendarSource`s at query time; rescheduling (API and drag) updates the one record and the calendar shows a single item at the new time |
| Calendar items open their original records | UI tests: clicking an item, and dashboard entries, open the event's details via `IRecordNavigator` |

Also delivered: standalone events with assignments, versioning, cancellation and audit fields
(`created_by`/`updated_by`); personal and management dashboards; filters and title search; event
editor; drag-to-reschedule in month and week/day views; quick create; colour-coded categories with
assignee initials. 115 automated tests pass.

Deferred / open:
- `GET /dashboard/overdue`: Phase 4 (nothing can be overdue until tasks and milestones exist).
- Global search: still disabled; scheduled for when projects, episodes and tasks exist.
- Category colours resolve when a page is built, so a theme change applies on the next visit to the page.
- Milestones, tasks, episodes and releases plug into the calendar as `ICalendarSource`s in Phases 4–6.

## Supporting documents

`PRODUCT_REQUIREMENTS.md` and `UI_SPECIFICATION.md` are covered by the specification itself
(Parts 1 and 6) and will be split out if they start to diverge from it.
