# Database Schema

Current, implemented schema. The target schema is defined in
[TECHNICAL_SPECIFICATION.md](TECHNICAL_SPECIFICATION.md#part-4--database-specification); conventions
are described in [TECHNICAL_ARCHITECTURE.md](TECHNICAL_ARCHITECTURE.md#persistence-conventions).
Every table has `id uuid` (UUIDv7) primary key and `created_at` / `updated_at timestamptz`.

## Migrations

| Migration | Phase | Changes |
|---|---|---|
| `InitialBaseline` | 0 | Establishes the migration history. No tables. |
| `AddIdentityAndPermissions` | 1 | Adds `users`, `roles`, `user_roles`, `permissions`, `role_permissions`, `refresh_tokens`. Additive only. |
| `AddCalendarEvents` | 3 | Adds `calendar_events`, `calendar_event_assignments`. Additive only. |
| `AddProjectsTasksAndActivity` | 4 | Adds `projects`, `project_members`, `project_milestones`, `tasks`, `task_assignments`, `activity_logs`, and the append-only trigger on `activity_logs`. Additive only. |
| `AddShowsAndEpisodes` | 5 | Adds `shows`, `seasons`, `episodes`, `episode_assignments`, nullable `tasks.episode_id` with its index, foreign key and the `ck_tasks_project_or_episode` check. Additive only. |
| `AddReleases` | 6 | Adds `distribution_channels` and `releases`. Additive only. Channels are seeded at startup (not in the migration). |
| `AddRemindersAndNotifications` | 7 | Adds `reminders`, `notifications`, `notification_outbox`, `notification_preferences`. Additive only. Hangfire creates and manages its own `hangfire` schema at startup. |
| `GoogleCalendarInvites` | 9 | Adds `google_calendar_connections`, `google_calendar_invites`, `google_calendar_invite_guests`. Additive only. |
| `RepeatingEvents` | 9 | Adds `event_series` and `calendar_events.series_id`. Additive only. |
| `WeekGoogleInvites` | 9 | `google_calendar_invites.calendar_event_id` becomes nullable; adds `session_week_id` (unique, → session_weeks) and the check `ck_google_calendar_invites_one_target`. Additive only. |
| `SessionWeeks` | 9 | Adds `session_weeks`, and `session_week_id`, `theme`, `speaker` on `calendar_events`. Additive only. |
| `ReleasesWithoutEpisode` | 9 | `releases.episode_id` becomes nullable; adds `releases.title` and the check `ck_releases_episode_or_title`. Additive only (existing rows keep their episode). |

## Tables

### users
| Column | Type | Notes |
|---|---|---|
| full_name | varchar(150) | |
| email | varchar(255) | unique; stored trimmed + lower-case |
| password_hash | text | ASP.NET Core Identity format |
| is_active | boolean | |
| timezone | varchar(100) | IANA id; defaults to `Organisation:DefaultTimeZone` |
| version | integer | optimistic concurrency |

### roles
`name varchar(50)` unique, `description text`. Seeded: Administrator, Production Manager, Producer, Team Member.

### user_roles
`user_id → users (cascade)`, `role_id → roles (restrict)`; unique `(user_id, role_id)`.

### permissions
`name varchar(100)` unique, `description text`. Seeded from `DOF.Domain.Rules.Permissions`.

### role_permissions
`role_id → roles (cascade)`, `permission_id → permissions (restrict)`; unique `(role_id, permission_id)`.

Default grants: Administrator = all; Production Manager = all except `users.manage`, `settings.manage`;
Producer = `projects.edit`, `episodes.manage`, `releases.schedule`, `tasks.assign`, `calendar.manage`;
Team Member = none.

### refresh_tokens
| Column | Type | Notes |
|---|---|---|
| user_id | uuid | → users (cascade); indexed |
| token_hash | varchar(64) | SHA-256 hex of the token; unique. The token itself is never stored. |
| family_id | uuid | shared by all rotations of one sign-in; indexed |
| expires_at | timestamptz | |
| revoked_at | timestamptz null | |
| revocation_reason | varchar(40) null | Rotated, LoggedOut, ReuseDetected, PasswordChanged, UserDeactivated |
| replaced_by_token_id | uuid null | successor after rotation |
| device_name | varchar(100) null | supplied by the client at sign-in |

### calendar_events
Standalone events only. Dates owned by episodes, milestones, tasks and releases are never copied here.

| Column | Type | Notes |
|---|---|---|
| title | varchar(200) | |
| description | text null | |
| event_type | varchar(40) | Meeting, MinistryEvent, Training, ExternalAppointment, RecordingSession, Other |
| start_at / end_at | timestamptz | UTC; end after start. All-day events span whole days in the organisation timezone (end exclusive). Index `(start_at, end_at)` |
| is_all_day | boolean | |
| location / meeting_url | text null | |
| priority | varchar(20) | Low, Normal, High, Urgent |
| status | varchar(40) | Scheduled, Completed, Cancelled (cancel instead of delete). Indexed |
| created_by / updated_by | uuid null | → users (restrict); set automatically |
| version | integer | optimistic concurrency |
| session_week_id | uuid null | → session_weeks (restrict; indexed). Set for the days of a planned session week |
| series_id | uuid null | → event_series (restrict; indexed). Set for the occurrences of a repeating event |
| theme | varchar(200) null | A session day's theme; the title is "‹week name›: ‹theme›" |
| speaker | varchar(200) null | Who is sharing, as typed (may be a guest who is not a user) |

### event_series
How a repeating event repeats. Its occurrences are ordinary `calendar_events` rows (each owns its date).
`frequency varchar(20)` (Daily, Weekly, Monthly), `interval int` (check 1–12), `weekdays varchar(80) null`
(day names for weekly series), `until date null` / `count int null` (exactly one: `ck_event_series_one_end`),
`created_by` / `updated_by`, `version`.

### session_weeks
A week of a recurring session series such as a Monday-to-Friday devotion. Its days are ordinary
`calendar_events` rows (the dates live there, not here). `name varchar(150)`, `week_start date` (check
`ck_session_weeks_starts_monday`), `event_type varchar(40)`, `location varchar(500) null`, `meeting_url
text null`, `speaker varchar(200) null` (who shares on days that name nobody else), `created_by` /
`updated_by`, `version`. Index `(name, week_start)`.

### calendar_event_assignments
`calendar_event_id → calendar_events (cascade)`, `user_id → users (restrict)`, `assignment_type varchar(30)`
(Organizer, Attendee); unique `(calendar_event_id, user_id)`; indexed on `user_id`.

### google_calendar_connections
The organisation's Google account for calendar invites (Phase 9). One active row at most (partial unique
index on `archived_at IS NULL`); disconnecting archives the row.

| Column | Type | Notes |
|---|---|---|
| account_email | varchar(320) | The Google account invites are sent from |
| encrypted_refresh_token | text | AES-256-GCM with `GoogleCalendar:TokenEncryptionKey`; never stored in plain text |
| calendar_id | varchar(320) | `primary` |
| connected_by | uuid | → users (restrict) |
| archived_at | timestamptz null | Set when disconnected |
| last_error / last_error_at | varchar(1000) null / timestamptz null | E.g. Google refused access; shown in Settings |

### google_calendar_invites
Google Calendar invites for one standalone event, or for a whole session week (`session_week_id`; one
repeating Google event for all its days). Exactly one of `calendar_event_id` / `session_week_id` is set
(`ck_google_calendar_invites_one_target`); each is unique. The dates are not copied: the Google event is
rebuilt from `calendar_events` on every push.

| Column | Type | Notes |
|---|---|---|
| calendar_event_id | uuid null | → calendar_events (restrict); unique. Null for a session-week invite |
| session_week_id | uuid null | → session_weeks (restrict); unique. Set for a whole-week invite |
| connection_id | uuid | → google_calendar_connections (restrict): the account that owns the Google event |
| google_event_id | varchar(100) | Chosen by DOF (`dof` + hex), so a retried insert can never duplicate the event |
| status | varchar(20) | Pending, Synced, Removing, Removed, Failed. Index `(status, next_attempt_at)` |
| synced_hash | varchar(64) null | SHA-256 of what Google last received; a different hash means the event changed |
| created_in_google | boolean | Later pushes are updates |
| last_synced_at / responses_checked_at | timestamptz null | |
| retry_count / next_attempt_at / last_error | integer / timestamptz / varchar(1000) null | Retries 1, 5, 15, 60 min, 4 h; Failed after 6 attempts |
| created_by / updated_by | uuid null | → users (restrict); set automatically |
| version | integer | optimistic concurrency |

### google_calendar_invite_guests
`invite_id → google_calendar_invites (cascade)`, `email varchar(320)` (lower-case), `user_id → users (restrict)`
(null for outside guests), `display_name varchar(200) null`, `response varchar(20)` (Pending, Accepted,
Tentative, Declined, as reported by Google); unique `(invite_id, email)`.

### projects
`name varchar(200)`, `description text null`, `project_type varchar(50)` (Documentary, ShowProduction,
Conference, Campaign, MusicProduction, Partnership, Other), `status varchar(40)` (Proposed, Approved,
Planning, Active, OnHold, Completed, Archived; indexed), `priority varchar(20)`, `manager_id → users
(restrict; indexed)`, `start_date` / `target_end_date date null`, `completed_at` / `archived_at
timestamptz null`, `created_by` / `updated_by`, `version`.

### project_members
`project_id → projects (cascade)`, `user_id → users (restrict)`, `project_role varchar(60)`,
`joined_at timestamptz`; unique `(project_id, user_id)`.

### project_milestones
`project_id → projects (restrict)`, `title varchar(200)`, `description text null`, `due_date date`
(indexed), `status varchar(40)` (NotStarted, InProgress, AwaitingReview, Completed, Blocked, Cancelled),
`priority varchar(20)`, `assigned_to → users null (restrict; indexed)`, `completed_at`, `created_by` /
`updated_by`, `version`.

### tasks
`title varchar(200)`, `description text null`, `project_id → projects null (restrict; indexed)`,
`due_at timestamptz null` (indexed), `priority varchar(20)`, `status varchar(40)` (as milestones;
indexed), `completed_at`, `completed_by → users null`, `episode_id → episodes null (restrict; indexed)`,
`created_by` / `updated_by`, `version`. Check `ck_tasks_project_or_episode`: a task has a project or an
episode, not both.

### task_assignments
`task_id → tasks (cascade)`, `user_id → users (restrict; indexed)`, `assigned_at`, `is_primary boolean`;
unique `(task_id, user_id)`. Exactly one primary per task (enforced by the domain).

### shows
`name varchar(200)`, `description text null`, `producer_id → users null (restrict; indexed)`,
`show_type varchar(40)` (Recurring, LimitedSeries, OneOff, SpecialProduction), `status varchar(40)`
(InDevelopment, Active, OnHiatus, Ended, Archived; indexed), `default_duration_minutes integer null`,
`archived_at timestamptz null`, `created_by` / `updated_by`, `version`.

### seasons
`show_id → shows (restrict)`, `season_number integer`, `name varchar(200) null`, `start_date` /
`end_date date null`, `status varchar(40)` (Planned, InProduction, Completed, Cancelled),
`created_by` / `updated_by`, `version`. Unique `(show_id, season_number)`.

### episodes
`show_id → shows (restrict; indexed)`, `season_id → seasons null (restrict)`, `episode_number integer
null`, `title varchar(200)`, `description text null`, `producer_id → users null (restrict; indexed)`,
`recording_start` / `recording_end timestamptz null` (start indexed), `recording_location varchar(200)
null`, `recorded_at timestamptz null`, `editing_due_date` / `review_due_date` / `approval_due_date date
null` (each indexed), `status varchar(40)` (Planned, Scheduled, Recorded, Editing, InReview,
AwaitingApproval, Approved, ReadyForRelease, PartiallyPublished, Published, Cancelled; indexed),
`archived_at timestamptz null` (set while cancelled), `created_by` / `updated_by`, `version`.
Unique `(season_id, episode_number)` where both are set, and unique `(show_id, episode_number)` for
episodes without a season. Checks: `ck_episodes_recording_range` (both recording times or neither, end
after start) and `ck_episodes_episode_number` (positive). `recording_location` goes beyond the
specification's column list to hold the "Location" shown on the episode screen (spec 6.3).

### episode_assignments
`episode_id → episodes (cascade)`, `user_id → users (restrict; indexed)`, `responsibility varchar(40)`
(Producer, Director, Presenter, Editor, Reviewer, PublishingManager, Crew), `assigned_at`. Unique
`(episode_id, user_id, responsibility)`: one person may hold several responsibilities. `Crew` covers the
episode screen's "assigned crew" (spec 6.3).

### distribution_channels
`name varchar(100)` (unique), `channel_type varchar(40)` (Video, Audio, Social, Broadcast, Web, Other),
`description text null`, `is_active boolean`, `created_by` / `updated_by`. Seeded at startup with
YouTube, Spotify, Facebook, Instagram, Television, Website and Other (added once by name, so later edits
are kept). Never deleted.

### releases
`episode_id → episodes null (restrict; indexed)`, `title varchar(200) null` (what is released when
there is no episode, e.g. a single or trailer; null for episode releases), `project_id → projects null (restrict; indexed)`,
`distribution_channel_id → distribution_channels (restrict)`, `scheduled_at timestamptz null` (indexed;
kept after publication), `actual_published_at timestamptz null`, `status varchar(40)` (Draft,
AwaitingApproval, Ready, Scheduled, Published, Postponed, Cancelled, Failed; indexed), `assigned_to →
users null (restrict; indexed)`, `external_url varchar(2000) null`, `notes text null`,
`postponement_reason` / `cancellation_reason text null`, `published_by → users null`, `created_by` /
`updated_by`, `version`. Unique `(episode_id, distribution_channel_id)` where `status <> 'Cancelled'`.
Checks: `ck_releases_published_has_time` (Published needs `actual_published_at`) and
`ck_releases_status_not_overdue` ("Overdue" is derived when reading and is never stored) and
`ck_releases_episode_or_title` (every release names an episode or a title). The unique index only limits
episode releases; typed-in content may have several releases per channel. `notes` and
`published_by` go beyond the specification's column list, to hold the screen's notes and who
confirmed publication.

### reminders
`user_id → users (restrict)`, `source_type varchar(40)` (Task, Milestone, CalendarEvent, Episode, Release),
`source_id uuid`, `offset_minutes integer null` (null = fixed time), `is_automatic boolean`,
`scheduled_at timestamptz`, `channel varchar(30)` (InApp, Email), `status varchar(30)` (Pending,
Processing, Delivered, Failed, Cancelled), `delivered_at`, `retry_count`, `last_error text null`,
`cancellation_reason varchar(200) null`, `processing_started_at`, `version`. Indexes `(status,
scheduled_at)` (dispatcher), `(source_type, source_id)`, `(user_id, status)`. Reminders are never deleted:
obsolete ones are cancelled with a reason.

### notifications
`user_id → users (restrict)`, `title varchar(200)`, `message text`, `notification_type varchar(30)`
(Reminder, Overdue, Escalation), `source_type varchar(40) null`, `source_id uuid null`, `is_read`,
`read_at`, `idempotency_key varchar(300)` **unique**. Index `(user_id, is_read, created_at)`.

### notification_outbox
`event_type varchar(60)` (SendEmail, SourceChanged), `payload jsonb`, `idempotency_key varchar(300)`
**unique**, `status varchar(30)` (Pending, Processing, Processed, Failed), `retry_count`,
`next_attempt_at` (indexed with status), `processed_at`, `last_error`. Processed rows are purged after
30 days.

### notification_preferences
`user_id → users (cascade; unique)`, `email_enabled`, `desktop_enabled`, `overdue_alerts`,
`reminder_offsets integer[]`.

### activity_logs
`user_id → users null (restrict)`, `entity_type varchar(40)`, `entity_id uuid`, `action varchar(40)`,
`old_values` / `new_values jsonb null`, `created_at`. Index `(entity_type, entity_id, created_at)` and
`(created_at)`. **Trigger `activity_logs_append_only`** rejects `UPDATE` and `DELETE`.
