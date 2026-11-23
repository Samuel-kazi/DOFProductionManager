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
- **Audit fields:** `AuditingInterceptor` (scoped) sets `created_at` / `updated_at` on every save and, for
  entities deriving from `AuditableEntity`, `created_by` / `updated_by` from `ICurrentUser`. Both are
  foreign keys to `users` with `RESTRICT`. Services never set these by hand.
- **Enums** are stored as strings (`varchar`), matching the specification's VARCHAR status columns.
  Because of that, the database sorts them alphabetically, so order by enum value in memory when it matters.
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
| `GoogleCalendar:ClientId`, `GoogleCalendar:ClientSecret` | Google OAuth client ("Desktop app" type) for calendar invites | **Secret.** env vars / secret store. Without them Google Calendar shows as not set up |
| `GoogleCalendar:TokenEncryptionKey` | Base64 of 32 bytes; encrypts stored Google refresh tokens and the sign-in state | **Secret.** env var / secret store. Changing it means reconnecting Google |
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

## Calendar (Phase 3)

**Single source of truth.** A date lives only in the record that owns it. `calendar_events` holds only
standalone events (meetings, ministry events, training, external appointments, unlinked recording
sessions). Everything else reaches the calendar through an `ICalendarSource`:

```text
CalendarService ──► ICalendarSource (one per record type)
                      ├─ StandaloneEventCalendarSource   (Phase 3)
                      ├─ MilestoneCalendarSource         (Phase 4, all-day on the due date)
                      ├─ TaskDeadlineCalendarSource      (Phase 4, a point at due_at)
                      ├─ episode recording, edit, review (Phase 5)
                      └─ releases                        (Phase 6)
```

Each source projects its records into `CalendarItemResponse` (`sourceType` + `sourceId` identify the
original; `id` is `"{sourceType}:{sourceId}"` plus a suffix where one record yields several items).
`CalendarService` validates the range (end after start, at most 190 days), asks every source whose
type passes the `sourceTypes` filter, and sorts the result. Items overlap the range when
`start < rangeEnd && end > rangeStart`. Cancelled items are excluded unless `includeCancelled` or a
status filter asks for them. Project and show filters match nothing in sources that have no project
or show. **To add a source in a later phase:** implement `ICalendarSource`, register it in
`Application/DependencyInjection.cs` and add its `sourceType` to `CalendarValues.SourceTypes`.
Rescheduling always goes through the owning record's own endpoint.

**Time zones.** Instants are UTC (`timestamptz`). The day/week/month/agenda endpoints and dashboards
compute boundaries in the signed-in user's timezone (or `timeZone=`), with weeks starting on Monday.
All-day events are snapped to whole days in the organisation timezone: midnight of the first day to the
midnight after the last day. A midnight end is treated as exclusive, so re-saving doesn't lengthen
the event (`ZonedTime.SnapToWholeDays`).

**Events.** Versioned (optimistic concurrency) and auditable. `DELETE` cancels; the row stays.
Create/update/cancel need `calendar.manage`; every signed-in user can read. Assignees must be active
users; `Organizer` marks the responsible person.

**Dashboards** reuse the calendar: personal = today's items and the next 7 days assigned to me;
management (`reports.view`) = all upcoming activities plus those with nobody assigned. Every entry is
a calendar item that links to its record (no decorative statistics). `/dashboard/overdue` arrives with
tasks and milestones in Phase 4, since nothing in the system can be overdue until then.

**Desktop calendar.** `CalendarViewModel` loads `GET /calendar` for the visible range (month = 6
Monday-first weeks) and builds month cells, week/day columns or agenda groups in the user's timezone.
Timed items are laid out by `Controls/TimeGridPanel` (48 px per hour) with overlapping items placed
side by side (`TimeSlotLayout.AssignLanes`); day-long items go in the all-day row. Items are coloured
by category (`Brush.Cat.*` tokens) and show assignee initials. Clicking an item opens its original
record: standalone events open in the details panel, and other sources will open their own screens
via `IRecordNavigator` (implemented by the shell). Dragging (`CalendarView.axaml.cs`) moves standalone
events by whole days in the month view, and by days plus 15-minute steps in week/day view, through
`PUT /calendar/events/{id}` with the current version, keeping the event's length. Filters (type,
priority, mine, cancelled) query the server; the title filter narrows what's loaded.

## Projects, tasks and history (Phase 4)

**Records.** Projects (versioned, auditable, archivable) own members and milestones. Tasks stand alone
or belong to a project and have one or more assignees, exactly one of them primary (the responsible
person); with no assignees given, the creator is primary. Milestones and tasks share the
`WorkStatus` values. Completing stamps `completed_at` (and `completed_by` for tasks); reopening clears
them, and the history keeps every step. Archived projects take no new milestones or tasks.

**Permissions** (`WorkAccess`, checked in the services for every change; responses carry `canEdit`):
- Projects: create with `projects.create`. Edit, change status, manage members and milestones with
  `projects.edit` or as the project's manager. Archiving or restoring needs `projects.archive`.
- Milestones: whoever can edit the project, plus the milestone's assignee.
- Tasks: anyone may create one for themselves. Giving work to someone new needs `tasks.assign`. The
  creator, the assignees and `tasks.assign` holders may change a task.
- Reading is open to every signed-in user.

**Activity log.** `ActivityRecorder` adds an `activity_logs` row in the same unit of work as the change
it describes (old and new values of only the fields that changed, as JSONB). Recorded:
create/update/status/reschedule/assignment changes for projects, milestones and tasks; member
changes; calendar event create/reschedule/status/cancel; user create/update/roles/status; role
permission changes. `ActivityQueries` phrases entries ("Status changed from X to Y"). The table is
**append-only at the database level**: a trigger rejects any `UPDATE` or `DELETE`.

**Overdue and workload.** Tasks are overdue when open and past `due_at`; milestones when open and
their due date is before today in the organisation timezone. `/dashboard/overdue` lists both;
`WorkloadService` counts open, overdue and due-this-week tasks and open milestones per active user.
Overdue detection here is a query. Notifying people about it is the Phase 7 background job.

**Desktop.** *Projects* page: card list (open tasks, overdue count, next milestone), detail with
milestones (tick to complete), tasks, members and history, plus project and milestone editors in the
side panel. *Tasks* page: My tasks / Assigned by me / All / Overdue / Completed, list or board, tick to
complete, a details panel with a status picker and history, and an editor where people without
`tasks.assign` can only assign themselves. Calendar milestones and task deadlines open their records
and can be dragged; the drag updates the milestone or task. Dashboards show my tasks, reviews waiting
for me, overdue work, active projects, upcoming milestones and team workload, and a workload row
opens that person's tasks. Status pickers ignore empty or repeated values, because a picker being torn
down can write back an empty value and would otherwise trigger a reload loop.

## Shows and episodes (Phase 5)

**Records.** A show (versioned, archivable) has numbered seasons and episodes. An episode belongs to its
show and optionally to one of that show's seasons (one-off productions have none), and owns its
recording session (`recording_start`/`end`, location), `recorded_at`, and editing, review and approval
deadlines. `episode_assignments` hold responsibilities (Producer, Director, Presenter, Editor,
Reviewer, PublishingManager, Crew). The episode's producer always holds a Producer assignment. Tasks
may belong to an episode instead of a project (`tasks.episode_id`, never both, enforced in the domain,
the validator and a database check).

**Lifecycle.** Any status can be set, including corrections; the activity log keeps every change and
an optional note. Entering a post-recording stage records `recorded_at`, and going back to Planned or
Scheduled clears it. A deadline counts as met once the episode reaches the next stage (editing → in
review, review → awaiting approval, approval → approved). An episode is overdue when a pending
deadline has passed, or when a recording session ended and the episode is still Planned or Scheduled.
Publication statuses (`PartiallyPublished`, `Published`) cannot be set by hand. Phase 6 will derive
them from release confirmations, so a scheduled release is never shown as published. Cancelling sets
`archived_at`; archived shows take no new seasons, episodes or episode tasks.

**Permissions** (`ShowAccess`): `episodes.manage` creates and archives shows and edits everything. A
show's producer edits the show, its seasons and its episodes. An episode's producer edits that
episode and its crew. Anyone assigned to an episode may change its production status. Reading is open
to every signed-in user.

**Calendar.** `EpisodeCalendarSource` reads the episode directly: a timed `Recording` item plus all-day
deadline items while each deadline is still pending, all opening the episode. Item ids are
`Episode:{id}:{type}`, so one episode can contribute several items without any copy. Rescheduling
updates the episode itself. The `showId` filter applies to episodes and to tasks belonging to episodes.

**Desktop.** *Shows & Episodes* page: show cards (season and episode counts, next recording), or "My
episodes". A show page lists each season with its episodes plus a "Without a season" group. The episode
page has a header with a status picker (publication statuses are never offered), a production timeline
(Planned → Recording → Editing → Review → Approval → Release preparation → Published), production
information, crew assignment, tasks ("Add task" opens a task already linked to the episode) and
history. Show, season and episode editors open in the side panel. Calendar episode items use their own
colours and can be dragged: the recording keeps its length and a deadline moves to the new day. The
task editor's "Belongs to" picker offers projects and episodes. Dashboards add *My episodes*, overdue
episodes and *Awaiting publication*.

## Releases (Phase 6)

**Records.** A release is one episode, or typed-in content named by its `title` (a single, trailer or
announcement), on one distribution channel, with its own time, status, assignee, link and notes
(optionally linked to a project). An episode has at most one live release per channel; releases without
an episode have no episode status to drive. Channels are seeded, managed in Settings with `settings.manage`, and deactivated rather than
deleted.

**Rules** (in `Release`): ordinary edits set only workflow statuses (Draft, AwaitingApproval, Ready,
Scheduled, Failed). Publishing, postponing and cancelling each have their own action. **Publication
needs an explicit confirmation** with the actual time, never in the future, and the scheduled time is
kept. Nothing publishes a release because its time has passed. **Overdue is derived at read time**: an
open release past its time without confirmation is flagged (`isOverdue`, `displayStatus`, the
calendar status, `/releases/overdue`, the dashboards), and a database check stops "Overdue" from ever
being stored. Postponing needs a reason and either moves the release or holds it until further notice.
Rescheduling a postponed or failed release schedules it again. Published and cancelled releases are
closed. The previous time and the reasons are recorded in the history.

**Episodes follow their releases.** After every release change, `ReleaseService` recomputes the
episode's status from its live (non-cancelled) releases in the same unit of work: some published →
PartiallyPublished, all → Published, none → back to ReadyForRelease if it had been marked published.
Each change is recorded on the episode with the note "Follows its releases". This is the only way an
episode reaches a publication status.

**Permissions** (`ReleaseAccess`): `releases.schedule` creates, edits, reschedules, postpones and
cancels. That permission or being the release's assignee allows confirming publication.

**Calendar.** `ReleaseCalendarSource` places each release at its scheduled time. Dragging calls
`/schedule` on the release itself, so the previous time is in the history and nothing is copied.

**Reminders.** Release reminders are computed from `releases.scheduled_at` by the Phase 7
reconciliation job, so rescheduling or postponing a release updates them (tested in Phase 7).

**Desktop.** *Releases* page with the views from spec 6.4 (All, Ready to publish, Scheduled, Published,
Postponed, Overdue, Cancelled), a "Mine" filter, search, and a line summarising finished-but-
undistributed content. The details panel has publish (with a confirmation tick box), reschedule,
postpone (with a reason), cancel and edit forms, plus history. The episode page has a *Distribution*
panel listing each channel's scheduled time, publication time and status, and a "Schedule a release"
button. Calendar release items can be dragged. The organisation dashboard shows upcoming and overdue
releases. Lists discard results from a slower earlier load, so switching views quickly never shows a
stale list (Releases and Tasks pages).

## Reminder engine (Phase 7)

**Server-side only.** Reminders, notifications and e-mails are created and delivered by background
jobs in the API process, so they work with every desktop client closed (spec 5.9). The desktop only
displays the result.

**Change capture.** `ReminderSourceInterceptor` (an EF Core save interceptor) writes a `SourceChanged`
row to `notification_outbox` in the same transaction whenever a task, milestone, event, episode or
release changes, or its assignments do. No code path can move a date and leave stale reminders.
Saving notification preferences queues the person's upcoming records the same way.

**Jobs** (spec Part 7), scheduled by **Hangfire with PostgreSQL storage** (schema `hangfire`). Each
Hangfire entry point in `ReminderJobRunner` calls a plain application service with the current time,
and tests call those services directly with chosen times.

| Job | Schedule (UTC) | Service | What it does |
|---|---|---|---|
| 1 Dispatcher | every minute | `ReminderDispatcher` (+ `OutboxProcessor` before and after) | Claims due reminders and delivers them: in-app → `notifications`; e-mail → outbox |
| 2 Overdue detection | every 15 min | `OverdueDetector` | Alerts the people responsible for overdue tasks, milestones and releases, once per item and due time |
| 3 Reconciliation | with the dispatcher, via the outbox | `ReminderReconciler` | Cancels obsolete pending reminders (with a reason), creates the new ones, never touches delivered ones |
| 4 Escalation | hourly | `EscalationJob` | Tasks overdue for longer than `Reminders:EscalationAfterHours` (24) go to production managers (else administrators), recorded in the task history |
| 5 Maintenance | daily 03:30 | `MaintenanceJob` | Purges old processed outbox rows and re-reconciles every upcoming record (60 days) |

**Reliability.**
- *Locks:* work is claimed with a single `UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING id` (`PostgresJobStore`), so concurrent workers or API instances never take the same item. Hangfire's `DisableConcurrentExecution` stops overlapping runs, and items stuck in Processing for 10 minutes are picked up again.
- *Duplicates:* every notification and outbox message has a unique `idempotency_key` (`reminder:{id}`, `email:reminder:{id}`, `overdue:task:{id}:{due ticks}:{user}`, `escalation:…`), enforced by unique indexes. A conflicting insert is handled explicitly, not ignored.
- *Retries and failures:* e-mails are retried after 1, 5, 15 and 60 minutes, then every 4 hours, up to `Reminders:MaxDeliveryAttempts` (5). The final failure is recorded on the outbox row and on the reminder (`Failed`, `retry_count`, `last_error`), shown in the reminder centre, and logged as an error. Hangfire's own retries are off because the jobs handle this themselves.

**E-mail.** `SmtpEmailSender` uses the `Email` settings. Keep the password in user-secrets or the
environment (`Email__Password`), never in a committed file. Without `Email:Host` and
`Email:FromAddress`, e-mail is reported as unavailable and cannot be turned on.

**Desktop.** The top-bar bell shows the unread count and recent notifications (polled every 30
seconds while the app is open); opening one marks it read and goes to its record. New notifications
pop up in the window (Avalonia `WindowNotificationManager`) when the person allows it. These pop-ups
appear only while the app is running; when it is closed, notifications wait in the app and arrive by
e-mail if enabled. The *Reminders* page (spec 6.6) lists upcoming, delivered, failed and cancelled
reminders (with the failure reason), lets people cancel pending ones, and holds notification
preferences: pop-ups, e-mail, overdue alerts and default reminder offsets. Task and release details
have "Remind me". The personal dashboard shows recent notifications.

## Reports, search and refinement (Phase 8)

**Reports** (`ReportService`, spec 5.10) are read-only server-side queries over the live records, all
with the same table shape (headline figures, columns, rows, record links) and formatted in the
caller's timezone. `CsvWriter` exports any report as RFC 4180 CSV (UTF-8 with BOM, with protection
against formula injection). All reports need `reports.view`.

**Global search** (`SearchService`, spec 6.1) matches names across projects, shows, episodes, tasks,
events and releases (case-insensitive, open records first, 6 per kind). The top bar searches as you
type, and every hit opens its record.

**Self-service settings** (spec 6.7): people change their own name and timezone (`PATCH /auth/me`),
choose the calendar's default view (stored locally), and manage notification channels and default
reminders in the reminder centre. Administrators manage accounts, roles and channels.

**Performance** (spec Part 9): `ReportTests.Main_screens_meet_the_performance_targets_with_a_realistic_volume`
seeds 40 projects with 200 milestones, 400 tasks, 150 events, 10 shows with 120 episodes and 360
releases on top of the other tests' data, and asserts the dashboard, calendar, task list, episode
details, search and weekly report stay within the targets. Reads use `AsNoTracking`, projections,
server-side filtering and paging. The reminder list's lookups were batched (one query per record type
instead of one per reminder).

**Deployment** is described in [DEPLOYMENT.md](DEPLOYMENT.md): container image and compose file, an
explicit migrations bundle, encrypted backups with retention and a scripted restore test, and
self-contained desktop packages.

## Schedule PDFs and Google Calendar invites (Phase 9)

**Schedule PDFs.** `ScheduleExportService` (Application) builds a `ScheduleDocument` from the aggregated
calendar, so a PDF shows exactly what the calendar shows, with the same filters. Items spanning several
days appear on each day. A year needs a longer range than the calendar API allows (190 days), so the
export uses `CalendarService.MaxExportRangeDays` (368). `ISchedulePdfRenderer` is implemented in
Infrastructure with QuestPDF (`QuestPdfScheduleRenderer`): a day agenda (portrait), a seven-day board
(landscape), a month grid (landscape) or a twelve-month heat map (portrait), each followed by the full
list grouped by day, with a colour legend, totals, filters, timezone and page numbers. Lato is bundled
with QuestPDF, so the server needs no fonts installed. The desktop saves the file through the system
save dialog, starting in `ExportFolder` from its `appsettings.json` (default: Documents).

**Google Calendar.** One organisation-wide Google account (`google_calendar_connections`) sends the
invites. The desktop runs the browser sign-in for a desktop app (RFC 8252): `GoogleSignIn` listens on a
random loopback port and opens Google's page. The server builds the address, keeps the PKCE verifier
inside an AES-GCM–encrypted `state` (bound to the user, 10 minutes), exchanges the code with the client
secret and stores only the encrypted refresh token (`AesSecretProtector`). Scopes: `openid email
calendar.events`.

**Session weeks.** A weekly series (e.g. Morning Devotion, Monday–Friday) is planned in one form:
`SessionWeekService` creates one standalone event per day, titled "‹name›: ‹theme›", with who is
sharing in `calendar_events.speaker` (the week's speaker unless a day names someone else). The
`session_weeks` row only holds what the days share and groups them for "Edit whole week"; the days stay
ordinary events, so reminders, Google invites (the description starts with "Sharing: …"), PDFs (people
column starts with "Sharing: …") and the calendar need nothing new. Editing updates the same events and
cancels days left out. The desktop opens it from the calendar's New event ▾ menu, copies the last
week of the same series (times, place, speakers; not themes), and the week view scrolls up to reveal
sessions before 07:00.

Invites exist for standalone events only. Their dates stay in `calendar_events`:
`GoogleInviteSync.Build` rebuilds the Google event from the record on each push, and a SHA-256 of the
payload (`synced_hash`) detects changes. The job runs every minute through `ReminderJobRunner`. It
pushes queued and changed invites, deletes the Google event when the DOF event is cancelled or invites
are removed, and reads guests' answers every 10 minutes. Every change is sent with `sendUpdates=all`,
so Google e-mails the guests and updates their calendars. Inserts use a DOF-chosen event id, so a
retried insert becomes an update (409 → GET → PUT), and updates keep the answers guests already gave.
Failures are recorded on the invite and retried (1, 5, 15, 60 min, 4 h; then Failed). Revoked access
is shown on the connection. Reconnecting the same account keeps updating existing events; invites
from another account fail with a message asking to send them again. `GoogleCalendarHttpApi` talks to
Google over plain HTTP (no Google SDK); tests replace it with `FakeGoogleCalendarApi`.

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

**Desktop UI tests** (`DOF.IntegrationTests/Desktop/DesktopUiTests` and `CalendarUiTests`, helpers in `Ui.cs`) run the real views and view models
in a headless Avalonia window (Skia rendering) against the real API: sign-in, navigation to every
section, keyboard shortcuts, session restore, offline start, deactivation, password change, theme, every
calendar view, opening records, the event editor, drag-to-reschedule with simulated mouse input,
filters, dashboards, quick create, the projects and tasks pages (`WorkUiTests`), shows, seasons and episodes (`ShowUiTests`), releases and channels (`ReleaseUiTests`), notifications and the reminder centre (`ReminderUiTests`), and reports, search and settings (`ReportUiTests`). Each UI test has
a 90-second limit. At the end of the run the fixture closes windows tests left open; if they are left
open, the headless platform's shutdown never completes.
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
