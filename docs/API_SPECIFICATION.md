# API Specification

Implemented endpoints. The full target API is in
[TECHNICAL_SPECIFICATION.md](TECHNICAL_SPECIFICATION.md#part-5--api-specification). The live
OpenAPI document is served at `/swagger` in Development.

Conventions: base path `/api/v1`; success bodies use `{ "success", "data", "message" }`; errors are
ProblemDetails (validation errors are `ValidationProblemDetails` with per-field `errors`). Send the
access token as `Authorization: Bearer <token>`.

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| GET | `/health` | Anonymous | Readiness, including database connectivity |
| GET | `/health/live` | Anonymous | Liveness |
| GET | `/api/v1/system/info` | Anonymous | API name, version, environment, server UTC time, default timezone |
| POST | `/api/v1/auth/login` | Anonymous, rate-limited | `{email, password, deviceName?}` → token pair + profile |
| POST | `/api/v1/auth/refresh` | Anonymous, rate-limited | `{refreshToken}` → new token pair (old one is revoked) |
| POST | `/api/v1/auth/logout` | Anonymous, rate-limited | `{refreshToken}` → 204; idempotent |
| GET | `/api/v1/auth/me` | Signed in | Profile with roles and effective permissions |
| PATCH | `/api/v1/auth/me` | Signed in | `{fullName, timeZone}`: change my own name and timezone (IANA); returns the profile |
| POST | `/api/v1/auth/change-password` | Signed in, rate-limited | `{currentPassword, newPassword}` → new token pair; other sessions end |
| GET | `/api/v1/users` | Signed in | Paged list; `page`, `pageSize` (≤ 100), `search`, `isActive` |
| GET | `/api/v1/users/{id}` | Signed in | User details |
| POST | `/api/v1/users` | `users.manage` | Create user → 201 with `Location` |
| PUT | `/api/v1/users/{id}` | `users.manage` | Update profile and roles; requires current `version` |
| PATCH | `/api/v1/users/{id}/status` | `users.manage` | `{isActive}`; deactivation ends sessions immediately |
| GET | `/api/v1/roles` | Signed in | Roles with their permissions |
| PUT | `/api/v1/roles/{id}/permissions` | `users.manage` | Replace a role's permission set |
| GET | `/api/v1/permissions` | Signed in | Permission catalogue |
| GET | `/api/v1/calendar` | Signed in | Aggregated items overlapping `start`–`end` (≤ 190 days). Filters below |
| GET | `/api/v1/calendar/day` | Signed in | `date` (default today) in the user's timezone |
| GET | `/api/v1/calendar/week` | Signed in | Monday–Sunday week containing `date` |
| GET | `/api/v1/calendar/month` | Signed in | `year`, `month` (default current) |
| GET | `/api/v1/calendar/agenda` | Signed in | `start` (default today), `days` (1–90, default 14) |
| GET | `/api/v1/calendar/events` | Signed in | Standalone events, paged; `search`, `from`, `to`, `includeCancelled` |
| POST | `/api/v1/calendar/events` | `calendar.manage` | Create → 201 |
| GET | `/api/v1/calendar/events/{id}` | Signed in | Event with assignments, creator, `version`, `canEdit` |
| PUT | `/api/v1/calendar/events/{id}` | `calendar.manage` | Update or reschedule; requires current `version` |
| DELETE | `/api/v1/calendar/events/{id}` | `calendar.manage` | Cancel (kept, leaves the calendar) → 204 |
| GET | `/api/v1/dashboard/personal` | Signed in | Today's schedule and next 7 days assigned to me |
| GET | `/api/v1/dashboard/management` | `reports.view` | Upcoming activities and unassigned ones; optional `start`/`end` dates (≤ 60 days) |
| GET | `/api/v1/dashboard/upcoming` | Signed in | Next `days` (1–60, default 7); `mine=true` for mine only |

| GET | `/api/v1/projects` | Signed in | Paged summaries; `search`, `statuses`, `projectType`, `managerId`, `memberId`, `includeArchived` |
| POST | `/api/v1/projects` | `projects.create` | Create → 201; the manager becomes a member |
| GET | `/api/v1/projects/{id}` | Signed in | Detail with members and milestones, `canEdit`, `canArchive` |
| PUT | `/api/v1/projects/{id}` | edit rights¹ | Update; requires `version` |
| PATCH | `/api/v1/projects/{id}/status` | edit rights¹ (archive/restore: `projects.archive`) | `{status, version?}` |
| GET | `/api/v1/projects/{id}/timeline` | Signed in | Milestones, tasks and full history of the project and its work |
| POST | `/api/v1/projects/{id}/members` | edit rights¹ | `{userId, projectRole}`; adds or changes role |
| DELETE | `/api/v1/projects/{id}/members/{userId}` | edit rights¹ | Remove (not the manager) |
| GET / POST | `/api/v1/projects/{id}/milestones` | Signed in / edit rights¹ | List / create → 201 |
| GET | `/api/v1/milestones/{id}` | Signed in | Milestone |
| PUT | `/api/v1/milestones/{id}` | edit rights¹ or assignee | Update incl. due date and status; requires `version` |
| PATCH | `/api/v1/milestones/{id}/complete` | edit rights¹ or assignee | Complete |
| GET | `/api/v1/tasks` | Signed in | Paged; `assigneeId`, `createdById`, `assignedByMe`, `projectId`, `episodeId`, `statuses`, `open`, `overdue`, `search` |
| GET | `/api/v1/tasks/my-tasks` | Signed in | My open tasks |
| GET | `/api/v1/tasks/overdue` | Signed in | Open tasks past due; `mine=true` |
| POST | `/api/v1/tasks` | Signed in² | Create → 201 |
| GET | `/api/v1/tasks/{id}` | Signed in | Task with assignees, `isOverdue`, `canEdit` |
| PUT | `/api/v1/tasks/{id}` | task editors³ | Update incl. due date and assignees; requires `version` |
| PATCH | `/api/v1/tasks/{id}/status` | task editors³ | `{status, version?}`; Completed records who/when |
| POST | `/api/v1/tasks/{id}/assignments` | task editors³ ² | `{userId, isPrimary}` |
| DELETE | `/api/v1/tasks/{id}/assignments/{userId}` | task editors³ ² | Remove an assignee (not the last) |
| GET | `/api/v1/tasks/{id}/history` | Signed in | History, newest first |
| GET | `/api/v1/dashboard/overdue` | Signed in | Overdue tasks, milestones, episodes and releases; `mine=true` |
| GET | `/api/v1/users/{id}/workload` | Signed in | Counts, open tasks and open milestones |

¹ `projects.edit` or the project's manager. ² Assigning anyone other than yourself needs `tasks.assign`.
³ The task's creator, its assignees, or `tasks.assign` holders.

Tasks take an optional `episodeId` on create and update. A task belongs to a project **or** an episode,
never both (400 when both are sent; 422 for a missing or cancelled episode). Task responses include
`episode {id, title, showId, showName, code}`.

### Shows, seasons and episodes (Phase 5)

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| GET | `/api/v1/shows` | Signed in | Paged summaries; `search`, `statuses`, `showType`, `producerId`, `includeArchived` |
| POST | `/api/v1/shows` | `episodes.manage` | Create → 201 |
| GET | `/api/v1/shows/{id}` | Signed in | Show with seasons, `canEdit`, `canManage` |
| PUT | `/api/v1/shows/{id}` | show editors⁴ | Update; requires `version` |
| PATCH | `/api/v1/shows/{id}/status` | show editors⁴ (archive/restore: `episodes.manage`) | `{status, version?}` |
| GET | `/api/v1/shows/{id}/history` | Signed in | History of the show and its seasons |
| GET / POST | `/api/v1/shows/{id}/seasons` | Signed in / show editors⁴ | List / create → 201 (no number → next season; duplicate → 409) |
| GET / PUT | `/api/v1/seasons/{id}` | Signed in / show editors⁴ | Season; update incl. number and status (requires `version`) |
| GET | `/api/v1/shows/{id}/episodes` | Signed in | All the show's episodes in season/number order, seasonless last |
| POST | `/api/v1/shows/{id}/episodes` | show editors⁴ | One-off episode without a season → 201 |
| GET | `/api/v1/seasons/{id}/episodes` | Signed in | The season's episodes |
| POST | `/api/v1/seasons/{id}/episodes` | show editors⁴ | Create → 201 (no number → next in season; duplicate → 409) |
| GET | `/api/v1/episodes` | Signed in | Paged; `showId`, `seasonId`, `assigneeId`, `mine`, `statuses`, `open`, `search` |
| GET | `/api/v1/episodes/{id}` | Signed in | Episode with crew, production timeline, tasks, `isOverdue`, `canEdit`, `canChangeStatus` |
| PUT | `/api/v1/episodes/{id}` | episode editors⁵ | Update incl. season, number, producer, recording and deadlines; requires `version` |
| PATCH | `/api/v1/episodes/{id}/status` | episode editors⁵ or crew | `{status, version?, recordedAt?, note?}` |
| POST | `/api/v1/episodes/{id}/assignments` | episode editors⁵ | `{userId, responsibility}` (Producer, Director, Presenter, Editor, Reviewer, PublishingManager, Crew) |
| DELETE | `/api/v1/episodes/{id}/assignments/{assignmentId}` | episode editors⁵ | Remove (not the current producer's Producer assignment) |
| GET | `/api/v1/episodes/{id}/history` | Signed in | History, newest first |

⁴ `episodes.manage` or the show's producer. ⁵ Show editors plus the episode's producer.

Episode rules: an episode can only join a season of its own show; numbers are unique within a season
(and within a show's seasonless episodes). A recording start without an end uses the show's usual
length (or 60 minutes). Deadlines must run editing ≤ review ≤ approval. Any status can be set,
including corrections back to an earlier stage; entering a post-recording stage records `recordedAt`
(the given time, else the end of a past session, else now; never in the future), and returning to
Planned or Scheduled clears it. `Scheduled` needs a recording time. `PartiallyPublished` and
`Published` cannot be set by hand: they will follow release confirmations (Phase 6). Every change is
in the history, with the optional `note` on status changes.

### Releases and channels (Phase 6)

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| GET | `/api/v1/releases` | Signed in | Paged; `view` (All, Ready, Scheduled, Published, Postponed, Overdue, Cancelled), `episodeId`, `showId`, `channelId`, `assignedTo`, `mine`, `from`/`to`, `search` |
| POST | `/api/v1/releases` | `releases.schedule` | `{episodeId, distributionChannelId, scheduledAt?, status, assignedTo?, projectId?, externalUrl?, notes?}` → 201; one live release per episode and channel (409) |
| GET | `/api/v1/releases/{id}` | Signed in | Release with `status`, `displayStatus`, `isOverdue`, `canEdit`, `canConfirm` |
| PUT | `/api/v1/releases/{id}` | `releases.schedule` | Details and workflow status (Draft, AwaitingApproval, Ready, Scheduled, Failed); requires `version` |
| PATCH | `/api/v1/releases/{id}/schedule` | `releases.schedule` | `{scheduledAt, version?}`; a postponed or failed release becomes Scheduled |
| PATCH | `/api/v1/releases/{id}/publish` | `releases.schedule` or the assignee | `{actualPublishedAt, confirmed: true, externalUrl?, version?}`; not in the future; repeat to correct |
| PATCH | `/api/v1/releases/{id}/postpone` | `releases.schedule` | `{reason, newScheduledAt?, version?}`; without a new time it waits "until further notice" |
| PATCH | `/api/v1/releases/{id}/cancel` | `releases.schedule` | `{reason, version?}`; not for published releases |
| GET | `/api/v1/releases/{id}/history` | Signed in | History, newest first |
| GET | `/api/v1/releases/overdue` | Signed in | Open releases past their time without confirmation; `mine=true` |
| GET | `/api/v1/releases/unpublished` | Signed in | Finished episodes (approved, ready, partially published): their open releases, and those with no release |
| GET | `/api/v1/episodes/{id}/releases` | Signed in | The episode's distribution schedule, one entry per channel |
| GET | `/api/v1/distribution-channels` | Signed in | Active channels (`includeInactive=true` for all) |
| POST / PUT | `/api/v1/distribution-channels[/{id}]` | `settings.manage` | Add / rename, retype or (de)activate; never deleted |

Release rules: a release is **never** published by its time passing. Publishing needs `confirmed: true`
and the actual time, and keeps the scheduled time. `Overdue` is derived, not stored: an open release
(Draft, AwaitingApproval, Ready, Scheduled, Postponed) whose time has passed has `isOverdue: true` and
`displayStatus: "Overdue"`, and the flag clears when it is rescheduled or published. Published and
cancelled releases are closed. Each change is in the history, including the previous time on
reschedule or postpone and the reason for postponing or cancelling. After every release change the
episode's status follows its live releases: some published → `PartiallyPublished`, all → `Published`.

The personal dashboard now also returns `myTasks`, `pendingReviews` (tasks you created that await
review) and `myEpisodes` (open episodes you produce or crew). The management dashboard adds
`activeProjects`, `upcomingMilestones` (14 days), `overdue` (tasks, milestones, episodes),
`teamWorkload`, `awaitingPublication` (approved, ready or partially published episodes) and
`upcomingReleases` (next 14 days). `overdue` now also lists releases. Calendar items from milestones have `sourceType: "Milestone"`, `eventType: "Milestone"`
and are all-day. Task deadlines have `sourceType: "Task"`, `eventType: "TaskDeadline"`, no `endAt`, and
the project (or "Show: Episode") name in `location`. Episodes contribute `sourceType: "Episode"` items
with ids `Episode:{id}:{eventType}`: a timed `Recording` (location = recording location) and all-day
`EditingDeadline`, `ReviewDeadline` and `ApprovalDeadline` items, only while the episode has not yet
passed that stage. Episode items have priority `Normal`, match `showId`, and never match `projectId`. Releases contribute
`sourceType: "Release"`, `eventType: "Release"` items at `scheduledAt` (no `endAt`; the channel name is in
`location`) with `status` set to the stored status or `Overdue`. They match `showId`, `projectId`
(the release's project) and `assignedUserId`, and a `statuses=Overdue` filter matches the derived
status. Postponed releases without a time are not on the calendar.

### Reminders and notifications (Phase 7)

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| GET | `/api/v1/reminders` | Signed in (own) | Paged, upcoming first; `statuses` (Pending, Processing, Delivered, Failed, Cancelled), `sourceType`, `sourceId` |
| POST | `/api/v1/reminders` | Signed in | `{sourceType, sourceId, offsetMinutes? \| at?, channel}` → 201. Exactly one of offset (minutes before the record's time; follows the record) or fixed time; must be in the future |
| GET / PUT | `/api/v1/reminders/{id}` | Owner | Reminder; change a pending personal reminder (`version` required). Automatic ones follow the defaults |
| DELETE | `/api/v1/reminders/{id}` | Owner | Cancel a pending reminder → 204 (kept as Cancelled) |
| GET / PUT | `/api/v1/notification-preferences` | Signed in | `{emailEnabled, desktopEnabled, overdueAlerts, reminderOffsets[]}` (≤ 6 offsets); `emailAvailable` reports whether the server can send e-mail. Saving recalculates upcoming reminders |
| GET | `/api/v1/notifications` | Signed in (own) | Paged, newest first; `unreadOnly` |
| GET | `/api/v1/notifications/unread-count` | Signed in | `{count}` |
| PATCH | `/api/v1/notifications/{id}/read` | Owner | → 204 |
| PATCH | `/api/v1/notifications/read-all` | Signed in | → `{count: 0}` |

Reminders are created, recalculated and delivered **only on the server**. Responsible people get
automatic reminders from their default offsets (1 hour and 1 day before, unless changed) for tasks
(assignees), milestones (assignee, at 09:00 organisation time on the due day), events (attendees),
recordings (producer and crew) and releases (assignee, or the episode's producer). Standard offsets:
at the time, 15 min, 1 hour, 1, 3 and 7 days before. Notification types are Reminder, Overdue and
Escalation, each linked to its record (`sourceType`/`sourceId`).

### Reports and search (Phase 8)

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| GET | `/api/v1/reports/weekly` | `reports.view` | Monday–Sunday week containing `date`: completed and missed tasks, completed milestones, recorded episodes, published and missed releases |
| GET | `/api/v1/reports/monthly` | `reports.view` | Same for `year`/`month` |
| GET | `/api/v1/reports/upcoming-releases` | `reports.view` | Releases in the next `days` (1–180, default 30) |
| GET | `/api/v1/reports/unpublished-episodes` | `reports.view` | Recorded but not yet published everywhere, with days since recording and published/total channels |
| GET | `/api/v1/reports/overdue-tasks` | `reports.view` | Open tasks past due, oldest first, with days overdue |
| GET | `/api/v1/reports/project-status` | `reports.view` | Projects (not archived) with open/overdue work and milestone progress |
| GET | `/api/v1/reports/team-workload` | `reports.view` | Per person: open/overdue/this-week tasks, milestones, open episodes and releases |
| GET | `/api/v1/reports/publication-history` | `reports.view` | Published releases `from`–`to` (default 90 days, ≤ 400) with delay against the schedule |
| GET | `/api/v1/search` | Signed in | `q` (≥ 2 characters): up to 6 each of projects, shows, episodes, tasks, events and releases |

Every report accepts `timeZone` (default: the caller's) and `format=csv`. JSON reports are
`{key, title, description, period, timeZone, generatedAt, summary[{label, value}], columns[], rows[][],
links[{sourceType, sourceId}|null]}`, with cells already formatted (`yyyy-MM-dd HH:mm`). CSV is
RFC 4180, UTF-8 with BOM. Cells that a spreadsheet would run as a formula are prefixed with `'`.

**Calendar filters** (all optional, repeatable where plural): `eventTypes`, `sourceTypes`, `statuses`,
`priorities`, `assignedUserId`, `projectId`, `showId`, `includeCancelled`, and `timeZone` (IANA, for the
day/week/month/agenda boundaries).

**Calendar item:** `{ id, title, sourceType, sourceId, eventType, startAt, endAt, isAllDay, status,
priority, location, assignedUsers[{userId, fullName, assignmentType}], canEdit }`. `sourceType` +
`sourceId` identify the original record, which is where the item must be opened or rescheduled.

Status codes: 400 validation, 401 not signed in / bad credentials, 403 missing permission,
404 not found, 409 duplicate or stale `version`, 422 business rule (e.g. last Administrator),
429 rate limit.

All endpoints listed in the specification (Phases 1–8) are implemented.
