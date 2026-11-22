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

### calendar_event_assignments
`calendar_event_id → calendar_events (cascade)`, `user_id → users (restrict)`, `assignment_type varchar(30)`
(Organizer, Attendee); unique `(calendar_event_id, user_id)`; indexed on `user_id`.

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

### activity_logs
`user_id → users null (restrict)`, `entity_type varchar(40)`, `entity_id uuid`, `action varchar(40)`,
`old_values` / `new_values jsonb null`, `created_at`. Index `(entity_type, entity_id, created_at)` and
`(created_at)`. **Trigger `activity_logs_append_only`** rejects `UPDATE` and `DELETE`.
