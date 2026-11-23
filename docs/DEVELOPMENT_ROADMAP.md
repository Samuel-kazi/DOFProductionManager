# Development Roadmap

Phases are defined in [TECHNICAL_SPECIFICATION.md](TECHNICAL_SPECIFICATION.md#part-10--development-phases).
A phase is complete only when it passes its acceptance criteria and the quality gates in Part 11.

| Phase | Scope | Status |
|---|---|---|
| 0 | Project foundation | ✅ Complete (2026-11-22) |
| 1 | Authentication and users | ✅ Complete (2026-11-22) |
| 2 | Desktop foundation | ✅ Complete (2026-11-22) |
| 3 | Dashboard and calendar | ✅ Complete (2026-11-22) |
| 4 | Tasks and projects | ✅ Complete (2026-11-22) |
| 5 | Shows and episodes | ✅ Complete (2026-11-22) |
| 6 | Release management | ✅ Complete (2026-11-22); reminder criterion verified in Phase 7 |
| 7 | Reminder engine | ✅ Complete (2026-11-22) |
| 8 | Reports and refinement | ✅ Complete (2026-11-22); UAT and server go-live are for DOF to run |
| 9 | Schedule PDFs and Google Calendar invites (added after the specification) | ✅ Complete (2026-11-23); a live Google run needs DOF's Google OAuth client |

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

## Phase 4 — Tasks and projects ✅

| Acceptance criterion | Evidence |
|---|---|
| Assigned tasks appear in personal dashboards | API test (`MyTasks`) and UI test (dashboard "My tasks", tick to complete) |
| Milestones appear on the calendar | `MilestoneCalendarSource`; API test checks all-day placement, filters and moving without duplication; UI test opens and drags milestones and task deadlines |
| Completed records preserve history | Completion stamps who/when; reopening clears them while history keeps every step (API and UI tests). The activity log is append-only, enforced by a database trigger (test proves UPDATE/DELETE are rejected) |

Also delivered: project members and roles, project timelines, task lists for each view plus board,
assignment rules, overdue lists, team workload, `/users/{id}/workload`, and history for calendar,
user and role changes. 140 automated tests pass.

Deferred / open:
- Tasks linked to episodes (`episode_id`): done in Phase 5.
- Overdue *notifications* and escalation: Phase 7 (background jobs). Overdue *lists* exist now.
- Global search remains disabled.

## Phase 5 — Shows and episodes ✅

| Acceptance criterion | Evidence |
|---|---|
| A show can have multiple seasons | API test creates numbered seasons (auto-numbering, duplicates → 409, renumbering); UI test builds a show and season from the Shows page |
| Seasons can have multiple episodes | API test: numbered episodes per season, duplicates rejected, one-off episodes without a season, no moving into another show's season |
| Episodes can have different producers and editors | API test: two episodes in one season with different producers and editors; per-episode permissions (producers edit their own, crew change status only); UI test assigns an editor |
| Recording dates appear in the calendar | `EpisodeCalendarSource`; API test checks the timed recording and deadline items, show/assignee filters, moving without duplication, deadlines leaving as stages complete, cancelled episodes leaving; UI test opens the episode from the calendar and drags the recording |

Also delivered: production deadlines and overdue episodes, the episode lifecycle with corrections and
notes in the history, the production timeline, tasks linked to episodes (including the calendar's show
filter and drag keeping the link), *My episodes*, and *Awaiting publication*. 162 automated tests pass.

Deferred / open:
- `GET /episodes/{id}/releases` and the episode screen's distribution panel: done in Phase 6.
- Publication statuses now follow release confirmations (Phase 6).
- Global search is still disabled (spec 6.1); no phase assigns it yet.

## Phase 6 — Release management ✅

| Acceptance criterion | Evidence |
|---|---|
| One episode can have independent release dates across channels | API test: YouTube, Spotify and Television with their own times and statuses; publishing one leaves the others unchanged; the episode moves to PartiallyPublished, then Published; duplicate channel → 409. UI test schedules from the episode page |
| Published episodes preserve actual publication timestamps | API test: publishing needs `confirmed: true` and a time that is not in the future; both scheduled and actual times are kept; the assignee may confirm; published releases cannot be rescheduled or cancelled; corrections are in the history. A database check requires a time on Published |
| Missed releases are flagged | API test: a past, unconfirmed release is `isOverdue`/`displayStatus: Overdue` in the release, `/releases/overdue`, the Overdue view, the dashboard and the calendar; it is **not** published; rescheduling clears the flag. UI test shows the Overdue view |
| Rescheduled releases update their reminders | Verified in Phase 7: `ReminderTests.Rescheduled_and_postponed_releases_update_their_reminders` (rescheduling moves the pending reminder; postponing until further notice cancels it) |

Also delivered: the seven seeded channels and channel management in Settings, workflow statuses,
postponement and cancellation with reasons, `/releases/unpublished` (production finished, distribution
not), the episode distribution panel, upcoming and overdue releases on the dashboard, and calendar
drag for releases. Fixed: switching views quickly on the Tasks page could show a stale list.
183 automated tests pass.

Deferred / open:
- Reminder updates on reschedule: done in Phase 7.
- Overdue *notifications*: done in Phase 7 (job 2).
- Global search is still disabled.

## Phase 7 — Reminder engine ✅

| Acceptance criterion | Evidence |
|---|---|
| Reminders work when desktop clients are closed | `ReminderTests.Reminders_are_delivered_by_the_server_in_app_and_by_email_with_no_client_open`: only the server's jobs run, and the person gets the in-app notification and the e-mail. Verified live: Hangfire registers and runs the jobs against PostgreSQL (`Hangfire_registers_the_recurring_jobs_in_postgresql`, plus a manual run on the dev database) |
| A changed source date recalculates pending reminders | Tasks and releases: old pending reminders are cancelled with "The record's time changed", new ones are created at the new time, personal offset reminders move with the record, completing or postponing cancels, and running it again changes nothing |
| Failed notifications are recorded | E-mail failure: retried with back-off, then `Failed` with retry count and error on the reminder and the outbox row, listed under Failed reminders and in the UI |
| Duplicate notifications are prevented | Four concurrent dispatchers deliver a reminder once (row locks + unique idempotency keys); overdue detection and escalation run repeatedly produce one alert per item and due time, and a new due time alerts again |

Also delivered: personal reminders (offset or fixed time), notification preferences (pop-ups,
e-mail, overdue alerts, default offsets), escalation to production managers recorded in history, the
notification bell with pop-ups, the reminder centre, "Remind me" on tasks and releases, and recent
notifications on the dashboard. Fixed: toolbar icons (quick create, sidebar toggle) were invisible
because their stroke binding did not resolve. 206 automated tests pass.

Notes:
- Pop-ups use Avalonia's in-window notifications while the app runs. When the app is closed,
  notifications wait in the app and go out by e-mail if enabled. OS notification-centre integration
  is not included.
- E-mail needs SMTP settings on the server (`Email:*`, password in secrets). Until then it is shown
  as unavailable.

## Phase 8 — Reports and refinement ✅

The specification lists Phase 8 deliverables but no acceptance criteria. Each deliverable is checked
as follows:

| Deliverable | Evidence |
|---|---|
| Management reports | Weekly, monthly, project status, overdue tasks (`ReportTests`; desktop Reports page with headline figures and a table whose rows open records) |
| Workload reports | Team workload: tasks, milestones, episodes and releases per person |
| Publication reports | Upcoming releases, recorded but unpublished, publication history with delay against the schedule |
| CSV export | `format=csv` on every report. Test checks the BOM, header, quoting and formula-injection guard; the desktop exports to a chosen file |
| Performance optimisation | Performance test on a seeded realistic volume meets every spec Part 9 target; batched reminder lookups |
| User acceptance testing | [USER_ACCEPTANCE_TESTING.md](USER_ACCEPTANCE_TESTING.md): scripted scenarios for every Definition of Done item, by role, with sign-off. **To be run by DOF staff** |
| Production deployment | [DEPLOYMENT.md](DEPLOYMENT.md), Dockerfile, compose file, migrations bundle and desktop packages (built locally); encrypted backup, restore and restore test **run locally against PostgreSQL**; the Release API checked in Production mode. The Docker image was not built here (no Docker on this machine) |

Refinements: global search in the top bar (spec 6.1), self-service name and timezone, default
calendar view, quick-create menu for shows and releases, and removal of the last placeholder page.
Fixed: report link text did not use the theme-aware colour. 214 automated tests pass.

Open for DOF:
- Run the UAT script with staff and sign it off.
- Provision the server, build the container there, configure SMTP, TLS and off-site backup copies.
- Sign and notarise the desktop packages for distribution.
- OS notification-centre integration (pop-ups are in-app) and full-text search are possible future work.

## Phase 9 — Schedule PDFs and Google Calendar invites ✅

Requested by DOF after the specification was approved: download the schedule as a PDF for a day, week,
month or year, and invite people to events through Google Calendar so their calendars update when the
event changes. Not in spec Part 10, so these acceptance criteria were set for the phase:

| Acceptance criterion | Evidence |
|---|---|
| The schedule downloads as a PDF for a day, week, month and year | `ScheduleExportTests.Each_period_downloads_as_a_pdf_named_after_its_range`; `SchedulePdfTests` render every period (and an empty one). The layouts were checked as images: day agenda, week board, month grid, year heat map, then details by day |
| The PDF shows what the calendar shows | Built from the aggregated calendar with the same filters (`…respects_filters`). Multi-day items appear on each day; a year covers more than the 190-day calendar range (`A_year_covers_more_than_the_normal_calendar_range`) |
| Invites reach people's Google Calendars | `Sending_invites_puts_the_event_on_the_guests_google_calendars_with_their_answers`: team members and outside guests, times in the organisation timezone, all-day events as dates, guests' answers shown |
| Changing the event updates their calendars; cancelling removes it | `Changing_the_event_in_dof_updates_everyones_google_calendar_once` (one update, then nothing), `Cancelling_the_event_or_removing_invites_takes_it_off_google_calendars` |
| Failures are recorded and retried | `When_google_is_unavailable_the_invite_is_retried_until_it_succeeds`, `Revoked_google_access_is_reported_on_the_connection` |
| Credentials are never stored in plain text | The refresh token is AES-256-GCM encrypted (`An_administrator_connects…_stored_encrypted`, `Secrets_round_trip_only_with_the_same_key_and_purpose`). The sign-in state cannot be forged or used by another user (`The_sign_in_state_cannot_be_forged…`). Only loopback redirects are accepted |
| Permissions are enforced on the server | Connecting needs `settings.manage`; sending and removing invites need `calendar.manage`; everyone signed in can see invites and download PDFs |
| Google's API is used correctly | `GoogleCalendarClientTests`: request formats, `sendUpdates=all`, idempotent insert (409 → update that keeps answers), already-deleted events, token refresh, PKCE |
| Desktop | `ScheduleAndGoogleUiTests`: PDF download from the Calendar page, sending and removing invites from event details, read-only view for team members, connecting Google in Settings through a simulated browser |

Also delivered: migration `GoogleCalendarInvites` (additive), the `google-invites-sync` job, activity
entries for invites and connections, `ExportFolder` in the desktop settings, and setup steps in
[DEPLOYMENT.md](DEPLOYMENT.md#google-calendar). The calendar toolbar search box was narrowed so the
toolbar still fits at 1280 px.

Follow-up requested by DOF: a release no longer has to be of an existing episode. The New release form
offers "No episode" (the default unless started from an episode) and a "What is being released" title,
for singles, trailers or announcements. Such releases have channels, times, statuses, overdue warnings,
publication confirmation, reminders (to the assignee, else whoever created it), calendar items, search
and reports like episode releases; they just have no episode status to update. Migration
`ReleasesWithoutEpisode` (additive). Evidence: `ReleaseTests.A_release_without_an_episode_is_typed_in…`,
`ReleaseUiTests.A_release_can_be_typed_in_without_an_episode`, `ReleaseDomainTests`.

Follow-up requested by DOF: weekly session series such as devotions. Calendar → New event ▾ → "Plan a
week of sessions…": the series name, week, time, place and who shares that week, then each day's theme
and (only when different) who shares that day. Saves one event per day; "Edit whole week" on any day
reopens it, and "Copy last week" reuses the last week of the series. Who is sharing shows in event
details, tooltips, Google invites and PDFs. Migration `SessionWeeks` (additive). Evidence:
`SessionWeekTests` (API: planning, editing in place and cancelling dropped days, validation and
permissions) and `SessionWeekUiTests` (snapshots 69–70). Series with seasons and episodes keep using
Shows & Episodes (recording times and releases). 254 automated tests pass.

Notes:
- Invites cover standalone calendar events. Recordings, deadlines and releases are owned by episodes,
  tasks and releases; inviting people to those is possible future work.
- Google was replaced by a fake in tests. A live run needs DOF's Google Cloud project and OAuth client
  (see DEPLOYMENT.md).
- `DesktopUiTests.A_stored_session_is_restored_on_startup` failed once during a full run (a request still
  in flight when the test disposed its services) and passed on re-runs. That test was not changed in this phase.

## Supporting documents

`PRODUCT_REQUIREMENTS.md` and `UI_SPECIFICATION.md` are covered by the specification itself
(Parts 1 and 6) and will be split out if they start to diverge from it.
