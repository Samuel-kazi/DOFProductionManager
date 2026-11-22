# DOF PRODUCTION MANAGER
## Technical Implementation Specification
**Version:** 1.0  
**Document type:** Software Engineering & Product Implementation Specification  
**Product owner:** Dawn of Faith Ministry  
**Architecture:** Standalone desktop application with cloud backend  
**Status:** Approved for technical planning

---

# PART 1 — PRODUCT AND ENGINEERING FOUNDATION

## 1. Product Overview

DOF Production Manager is an independent desktop application designed to coordinate the production operations of Dawn of Faith Ministry.

The application provides a central environment for managing:

- Organisational activities and events.
- Projects and project milestones.
- Recurring shows and episodes.
- Recording and post-production schedules.
- Television broadcasts and digital releases.
- Individual and team responsibilities.
- Reminders and notifications.
- Production reports and historical records.

The application must operate independently of all previous DOF software projects.

### 1.1 Core product principle

Every important activity must have:

1. A clearly defined record.
2. A responsible person.
3. A date or deadline where applicable.
4. A status.
5. A traceable history.
6. An appropriate notification mechanism.

### 1.2 Product architecture principle

The system must maintain a single source of truth.

The same recording date must not be stored independently in the episode, calendar and reminder systems.

Instead, the episode owns its recording date. The calendar reads that date, while the reminder engine uses it to schedule notifications.

The calendar is an aggregated representation of organisational commitments.

---

# PART 2 — TECHNOLOGY STACK

## 2. Approved Technology Choices

| Layer | Technology |
|---|---|
| Desktop application | Avalonia UI |
| Desktop language | C# |
| Desktop architecture | MVVM |
| Backend | ASP.NET Core Web API |
| Runtime | .NET 10 LTS |
| Database | PostgreSQL |
| ORM | Entity Framework Core |
| Database provider | Npgsql |
| API documentation | OpenAPI / Swagger |
| Background processing | Hangfire |
| Real-time communication | SignalR |
| Authentication | ASP.NET Core Identity with token-based API authentication |
| Logging | Serilog |
| Validation | FluentValidation |
| Testing | xUnit |
| Mocking | NSubstitute |
| Integration testing | ASP.NET Core WebApplicationFactory |
| Desktop packaging | Avalonia desktop packaging |
| Version control | Git |

Use stable, mutually compatible package versions. Pin dependency versions and update them deliberately.

Do not introduce unnecessary third-party dependencies.

### 2.1 Desktop framework

Use Avalonia UI because the application must primarily serve desktop users on macOS and Windows.

The desktop application must have:

- Native desktop window behaviour.
- Responsive layouts.
- Keyboard shortcuts.
- Persistent navigation.
- Light and dark themes.
- Native desktop notifications through an abstraction.
- Secure local credential storage.
- Automatic update support.
- A centralised API connection.

The desktop client must never connect directly to PostgreSQL.

### 2.2 Backend framework

Use ASP.NET Core Web API.

The backend owns:

- Authentication and authorisation.
- Business rules.
- Database operations.
- Calendar aggregation.
- Reminder scheduling.
- Notification delivery.
- Reporting.
- Audit logging.

### 2.3 Deployment model

The desktop application runs locally on each user's computer.

The backend and PostgreSQL database run on cloud infrastructure.

All users access the same online data.

---

# PART 3 — SOLUTION STRUCTURE

## 3. Repository Structure

Create a new repository named:

`DOFProductionManager`

Recommended structure:

```text
DOFProductionManager/
│
├── src/
│   │
│   ├── DOF.Desktop/
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Controls/
│   │   ├── Converters/
│   │   ├── Themes/
│   │   ├── Assets/
│   │   ├── Services/
│   │   └── App.axaml
│   │
│   ├── DOF.Api/
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   ├── Authentication/
│   │   ├── Hubs/
│   │   └── Program.cs
│   │
│   ├── DOF.Application/
│   │   ├── Projects/
│   │   ├── Calendar/
│   │   ├── Shows/
│   │   ├── Episodes/
│   │   ├── Releases/
│   │   ├── Tasks/
│   │   ├── Reminders/
│   │   ├── Notifications/
│   │   ├── Reports/
│   │   └── Common/
│   │
│   ├── DOF.Domain/
│   │   ├── Entities/
│   │   ├── Enums/
│   │   ├── Events/
│   │   ├── Exceptions/
│   │   └── Rules/
│   │
│   ├── DOF.Infrastructure/
│   │   ├── Persistence/
│   │   ├── Configurations/
│   │   ├── Repositories/
│   │   ├── BackgroundJobs/
│   │   ├── Notifications/
│   │   ├── Authentication/
│   │   └── ExternalServices/
│   │
│   └── DOF.Contracts/
│       ├── Requests/
│       ├── Responses/
│       └── Common/
│
├── tests/
│   ├── DOF.UnitTests/
│   ├── DOF.IntegrationTests/
│   └── DOF.ArchitectureTests/
│
├── docs/
│   ├── PRODUCT_REQUIREMENTS.md
│   ├── TECHNICAL_ARCHITECTURE.md
│   ├── DATABASE_SCHEMA.md
│   ├── API_SPECIFICATION.md
│   ├── UI_SPECIFICATION.md
│   └── DEVELOPMENT_ROADMAP.md
│
├── deployment/
│   ├── docker/
│   └── scripts/
│
├── .gitignore
├── README.md
└── DOFProductionManager.sln
```

### 3.1 Architecture boundaries

- Domain must not depend on Infrastructure.
- Application must not depend on Desktop.
- Desktop communicates with the backend through API clients.
- API uses Application services.
- Infrastructure implements database and external-service operations.
- Contracts define the communication between the desktop and backend.

Use dependency injection.

Avoid putting business logic inside controllers, ViewModels or UI components.

---

# PART 4 — DATABASE SPECIFICATION

## 4. Database conventions

Use PostgreSQL with UUID primary keys.

All tables should have:

- `id`
- `created_at`
- `updated_at`
- `created_by` where applicable
- `updated_by` where applicable

Use `timestamptz` for timestamps.

Use `date` for date-only values.

Use UTC for persisted timestamps.

The organisation's default timezone is:

`Africa/Nairobi`

Store timezone-aware event dates and convert them for display.

Use an integer `version` column for optimistic concurrency on important editable records.

Use archive fields rather than permanently deleting operational records.

## 4.1 Users and permissions

### users

| Column | Type | Description |
|---|---|---|
| id | UUID | Primary key |
| full_name | VARCHAR(150) | User name |
| email | VARCHAR(255) | Unique email |
| password_hash | TEXT | Secure password hash |
| is_active | BOOLEAN | Account status |
| timezone | VARCHAR(100) | User timezone |
| created_at | TIMESTAMPTZ | Creation |
| updated_at | TIMESTAMPTZ | Last update |

Authentication must use ASP.NET Core Identity-compatible password hashing.

Never store plain-text passwords.

### roles

| Column | Type |
|---|---|
| id | UUID |
| name | VARCHAR(50) |
| description | TEXT |

Seed the following roles:

- Administrator
- Production Manager
- Producer
- Team Member

### user_roles

- id
- user_id
- role_id

### permissions

Create a permissions system that can support individual permissions beyond the four default roles.

Examples:

- projects.create
- projects.edit
- projects.archive
- episodes.manage
- releases.schedule
- tasks.assign
- calendar.manage
- reports.view
- users.manage
- settings.manage

Role permissions must be configurable.

---

## 4.2 Project management

### projects

| Column | Type | Description |
|---|---|---|
| id | UUID | Primary key |
| name | VARCHAR(200) | Project name |
| description | TEXT | Project description |
| project_type | VARCHAR(50) | Project category |
| status | VARCHAR(40) | Project status |
| priority | VARCHAR(20) | Priority |
| manager_id | UUID | Project manager |
| start_date | DATE | Project start |
| target_end_date | DATE | Expected completion |
| completed_at | TIMESTAMPTZ | Actual completion |
| archived_at | TIMESTAMPTZ | Archive date |
| version | INTEGER | Concurrency |

Project statuses:

- Proposed
- Approved
- Planning
- Active
- OnHold
- Completed
- Archived

Project types:

- Documentary
- ShowProduction
- Conference
- Campaign
- MusicProduction
- Partnership
- Other

### project_members

- id
- project_id
- user_id
- project_role
- joined_at

### project_milestones

| Column | Type |
|---|---|
| id | UUID |
| project_id | UUID |
| title | VARCHAR(200) |
| description | TEXT |
| due_date | DATE |
| status | VARCHAR(40) |
| priority | VARCHAR(20) |
| assigned_to | UUID |
| completed_at | TIMESTAMPTZ |

Milestone statuses:

- NotStarted
- InProgress
- AwaitingReview
- Completed
- Blocked
- Cancelled

A project can have multiple milestones.

---

## 4.3 Shows and episodes

### shows

| Column | Type |
|---|---|
| id | UUID |
| name | VARCHAR(200) |
| description | TEXT |
| producer_id | UUID |
| show_type | VARCHAR(40) |
| status | VARCHAR(40) |
| default_duration_minutes | INTEGER |
| archived_at | TIMESTAMPTZ |

Show types:

- Recurring
- LimitedSeries
- OneOff
- SpecialProduction

### seasons

- id
- show_id
- season_number
- name
- start_date
- end_date
- status

Unique constraint:

`(show_id, season_number)`

### episodes

| Column | Type |
|---|---|
| id | UUID |
| show_id | UUID |
| season_id | UUID |
| episode_number | INTEGER |
| title | VARCHAR(200) |
| description | TEXT |
| producer_id | UUID |
| recording_start | TIMESTAMPTZ |
| recording_end | TIMESTAMPTZ |
| recorded_at | TIMESTAMPTZ |
| editing_due_date | DATE |
| review_due_date | DATE |
| approval_due_date | DATE |
| status | VARCHAR(40) |
| archived_at | TIMESTAMPTZ |
| version | INTEGER |

Episode statuses:

- Planned
- Scheduled
- Recorded
- Editing
- InReview
- AwaitingApproval
- Approved
- ReadyForRelease
- PartiallyPublished
- Published
- Cancelled

Do not assume that every episode must pass through every stage.

Allow authorised users to correct a status while preserving its history.

### episode_assignments

- id
- episode_id
- user_id
- responsibility
- assigned_at

Responsibilities may include:

- Producer
- Director
- Presenter
- Editor
- Reviewer
- PublishingManager

---

## 4.4 Distribution and releases

This is one of the most important parts of the application.

An episode may be distributed through several channels.

### distribution_channels

- id
- name
- channel_type
- description
- is_active

Seed:

- YouTube
- Spotify
- Facebook
- Instagram
- Television
- Website
- Other

### releases

| Column | Type |
|---|---|
| id | UUID |
| episode_id | UUID |
| project_id | UUID, nullable |
| distribution_channel_id | UUID |
| scheduled_at | TIMESTAMPTZ |
| actual_published_at | TIMESTAMPTZ |
| status | VARCHAR(40) |
| assigned_to | UUID |
| external_url | TEXT |
| postponement_reason | TEXT |
| cancellation_reason | TEXT |
| version | INTEGER |

Release statuses:

- Draft
- AwaitingApproval
- Ready
- Scheduled
- Published
- Postponed
- Overdue
- Cancelled
- Failed

Each episode can have many releases.

A release must have its own independent status.

For example, an episode can be published on YouTube while still awaiting its television broadcast.

The release record must preserve the scheduled date even after publication.

When postponed, record the previous date in the audit history.

---

## 4.5 Tasks and assignments

### tasks

| Column | Type |
|---|---|
| id | UUID |
| title | VARCHAR(200) |
| description | TEXT |
| project_id | UUID, nullable |
| episode_id | UUID, nullable |
| due_at | TIMESTAMPTZ |
| priority | VARCHAR(20) |
| status | VARCHAR(40) |
| created_by | UUID |
| completed_at | TIMESTAMPTZ |
| completed_by | UUID |
| version | INTEGER |

Task statuses:

- NotStarted
- InProgress
- AwaitingReview
- Completed
- Blocked
- Cancelled

### task_assignments

- id
- task_id
- user_id
- assigned_at
- is_primary

Support more than one assignee, but identify one primary responsible person.

A task can exist independently or belong to a project or episode.

Do not allow it to reference unrelated projects and episodes without validation.

---

## 4.6 Standalone calendar events

### calendar_events

This table stores events that do not already have a dedicated source record.

Examples:

- Staff meetings.
- General ministry events.
- Training sessions.
- External appointments.
- General recording sessions that are not yet linked to an episode.

Columns:

| Column | Type |
|---|---|
| id | UUID |
| title | VARCHAR(200) |
| description | TEXT |
| event_type | VARCHAR(40) |
| start_at | TIMESTAMPTZ |
| end_at | TIMESTAMPTZ |
| is_all_day | BOOLEAN |
| location | TEXT |
| meeting_url | TEXT |
| priority | VARCHAR(20) |
| status | VARCHAR(40) |
| created_by | UUID |
| version | INTEGER |

### calendar_event_assignments

- id
- calendar_event_id
- user_id
- assignment_type

The calendar API must aggregate:

1. Standalone calendar events.
2. Project milestones.
3. Task deadlines.
4. Episode recording dates.
5. Editing and review deadlines.
6. Scheduled releases.

Do not duplicate those records inside calendar_events.

Build a unified CalendarItem DTO from the original domain records.

---

## 4.7 Reminders and notifications

### reminders

| Column | Type |
|---|---|
| id | UUID |
| user_id | UUID |
| source_type | VARCHAR(40) |
| source_id | UUID |
| scheduled_at | TIMESTAMPTZ |
| channel | VARCHAR(30) |
| status | VARCHAR(30) |
| delivered_at | TIMESTAMPTZ |
| retry_count | INTEGER |
| created_at | TIMESTAMPTZ |

Reminder source types:

- Task
- Milestone
- CalendarEvent
- Episode
- Release

Reminder statuses:

- Pending
- Processing
- Delivered
- Failed
- Cancelled

Support configurable reminder offsets:

- At event time.
- 15 minutes before.
- 1 hour before.
- 1 day before.
- 3 days before.
- 7 days before.
- Custom date and time.

### notifications

- id
- user_id
- title
- message
- notification_type
- source_type
- source_id
- is_read
- created_at
- read_at

### notification_outbox

Use an outbox table to make notification delivery reliable.

Store:

- id
- event_type
- payload
- created_at
- processed_at
- retry_count
- status

A background worker processes pending notifications.

Prevent duplicate delivery using idempotency keys.

---

## 4.8 Supporting tables

### activity_logs

Record important operational changes.

Fields:

- id
- user_id
- entity_type
- entity_id
- action
- old_values JSONB
- new_values JSONB
- created_at

Examples:

- Project deadline changed.
- Episode marked recorded.
- Release postponed.
- Task reassigned.
- User permissions changed.

### attachments

Store references to supporting files or external URLs.

Fields:

- id
- entity_type
- entity_id
- file_name
- external_url
- content_type
- uploaded_by
- created_at

For the MVP, use links and lightweight document references.

Do not build a full media storage platform.

---

# PART 5 — API SPECIFICATION

## 5. API conventions

Base URL:

`/api/v1`

Use:

- REST conventions.
- JSON request and response bodies.
- UUID identifiers.
- Pagination for collections.
- Server-side validation.
- Consistent error responses.
- Cancellation tokens.
- Authentication on all protected endpoints.

Use ISO 8601 timestamps.

Return timestamps in UTC.

Use a standard response structure.

Example:

```json
{
  "success": true,
  "data": {},
  "message": null
}
```

For errors, use ProblemDetails.

---

## 5.1 Authentication endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| POST | /auth/login | Authenticate |
| POST | /auth/refresh | Refresh access token |
| POST | /auth/logout | Revoke refresh token |
| GET | /auth/me | Current user |
| POST | /auth/change-password | Change password |

Use short-lived access tokens and rotating refresh tokens.

Store refresh tokens securely and support revocation.

---

## 5.2 User endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| GET | /users | List users |
| GET | /users/{id} | User details |
| POST | /users | Create user |
| PUT | /users/{id} | Update user |
| PATCH | /users/{id}/status | Activate/deactivate |
| GET | /users/{id}/workload | Workload |
| GET | /roles | List roles |
| PUT | /roles/{id}/permissions | Update permissions |

Only authorised administrators may manage accounts and permissions.

---

## 5.3 Dashboard endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| GET | /dashboard/personal | Personal overview |
| GET | /dashboard/management | Organisation overview |
| GET | /dashboard/upcoming | Upcoming activities |
| GET | /dashboard/overdue | Overdue items |

Allow date range filtering.

---

## 5.4 Calendar endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| GET | /calendar | Aggregated calendar |
| GET | /calendar/day | Day view |
| GET | /calendar/week | Week view |
| GET | /calendar/month | Month view |
| GET | /calendar/agenda | Agenda view |
| GET | /calendar/events | Standalone events |
| POST | /calendar/events | Create event |
| GET | /calendar/events/{id} | Event details |
| PUT | /calendar/events/{id} | Update event |
| DELETE | /calendar/events/{id} | Archive/cancel event |

Calendar filters:

- Start date.
- End date.
- Event type.
- Project.
- Show.
- Assigned user.
- Status.
- Priority.

The aggregated calendar endpoint should return a common DTO.

Example:

```json
{
  "id": "uuid",
  "title": "Faith Frequency Recording",
  "sourceType": "Episode",
  "sourceId": "uuid",
  "eventType": "Recording",
  "startAt": "2026-12-01T07:00:00Z",
  "endAt": "2026-12-01T10:00:00Z",
  "status": "Scheduled",
  "assignedUsers": [],
  "canEdit": true
}
```

`sourceType` identifies the original record.

A calendar item must navigate to its original record.

---

## 5.5 Project endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| GET | /projects | List projects |
| POST | /projects | Create project |
| GET | /projects/{id} | Project details |
| PUT | /projects/{id} | Update project |
| PATCH | /projects/{id}/status | Change status |
| GET | /projects/{id}/timeline | Project timeline |
| GET | /projects/{id}/milestones | Milestones |
| POST | /projects/{id}/milestones | Create milestone |
| PUT | /milestones/{id} | Update milestone |
| PATCH | /milestones/{id}/complete | Complete milestone |
| POST | /projects/{id}/members | Add project member |

---

## 5.6 Show and episode endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| GET | /shows | List shows |
| POST | /shows | Create show |
| GET | /shows/{id} | Show details |
| PUT | /shows/{id} | Update show |
| GET | /shows/{id}/seasons | Show seasons |
| POST | /shows/{id}/seasons | Create season |
| GET | /seasons/{id}/episodes | List episodes |
| POST | /seasons/{id}/episodes | Create episode |
| GET | /episodes/{id} | Episode details |
| PUT | /episodes/{id} | Update episode |
| PATCH | /episodes/{id}/status | Change episode status |
| POST | /episodes/{id}/assignments | Assign personnel |
| GET | /episodes/{id}/releases | Distribution schedules |

Support episode creation for one-off productions that do not require a season.

---

## 5.7 Release endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| GET | /releases | List releases |
| POST | /releases | Create release |
| GET | /releases/{id} | Release details |
| PUT | /releases/{id} | Update release |
| PATCH | /releases/{id}/schedule | Reschedule |
| PATCH | /releases/{id}/publish | Confirm publication |
| PATCH | /releases/{id}/postpone | Postpone |
| PATCH | /releases/{id}/cancel | Cancel |
| GET | /releases/overdue | Overdue releases |
| GET | /releases/unpublished | Unpublished content |

The publish endpoint must require an actual publication confirmation.

Do not mark releases as published simply because their scheduled dates have passed.

---

## 5.8 Task endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| GET | /tasks | List tasks |
| POST | /tasks | Create task |
| GET | /tasks/{id} | Task details |
| PUT | /tasks/{id} | Update task |
| PATCH | /tasks/{id}/status | Update status |
| POST | /tasks/{id}/assignments | Assign user |
| GET | /tasks/my-tasks | Personal tasks |
| GET | /tasks/overdue | Overdue tasks |

---

## 5.9 Reminder and notification endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| GET | /reminders | List reminders |
| POST | /reminders | Create reminder |
| PUT | /reminders/{id} | Update reminder |
| DELETE | /reminders/{id} | Cancel reminder |
| GET | /notifications | List notifications |
| PATCH | /notifications/{id}/read | Mark read |
| PATCH | /notifications/read-all | Mark all read |
| GET | /notifications/unread-count | Unread count |

Reminder creation and delivery must be server-side.

---

## 5.10 Reporting endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| GET | /reports/weekly | Weekly report |
| GET | /reports/monthly | Monthly report |
| GET | /reports/upcoming-releases | Upcoming releases |
| GET | /reports/unpublished-episodes | Recorded but unaired |
| GET | /reports/overdue-tasks | Overdue tasks |
| GET | /reports/project-status | Project overview |
| GET | /reports/team-workload | Team workload |
| GET | /reports/publication-history | Publishing history |

Support CSV export in the reporting module.

---

# PART 6 — DESKTOP UI SPECIFICATION

## 6. Main application layout

Use a professional desktop layout.

### Navigation

The left sidebar should contain:

- Dashboard
- Calendar
- Projects
- Shows & Episodes
- Tasks
- Releases
- Reminders
- Reports

At the bottom:

- Team
- Settings
- Current user profile

The sidebar must be collapsible.

### Top navigation

Include:

- Global search.
- Quick create.
- Notifications.
- Current date.
- Connection status.
- User profile.

---

## 6.1 Dashboard screen

Create two dashboard modes.

### Personal dashboard

Components:

1. Greeting and current date.
2. Today's schedule.
3. My tasks.
4. Upcoming deadlines.
5. My assigned episodes.
6. Pending reviews.
7. Recent notifications.

### Management dashboard

Components:

1. Active projects.
2. Upcoming releases.
3. Upcoming production activities.
4. Overdue items.
5. Shows awaiting publication.
6. Team workload.
7. Upcoming milestones.
8. Recent operational changes.

Dashboard cards must navigate to the relevant records.

Avoid decorative statistics that do not lead to useful information.

---

## 6.2 Calendar screen

The calendar is a major feature and should receive significant development attention.

Provide:

- Month view.
- Week view.
- Day view.
- Agenda view.
- Filter panel.
- Event creation.
- Event details.
- Drag-and-drop rescheduling where appropriate.
- Colour-coded categories.
- Assigned-user indicators.
- Search and filtering.

### Calendar interaction

Clicking a calendar item should open its original record.

Examples:

- Clicking an episode recording opens the episode.
- Clicking a milestone opens the project milestone.
- Clicking a release opens the distribution record.
- Clicking a task deadline opens the task.

Never create a duplicate event when changing the date of an existing source record.

---

## 6.3 Episode screen

Each episode must have:

### Header

- Show name.
- Season.
- Episode number.
- Title.
- Producer.
- Status.

### Production timeline

- Planned.
- Recording.
- Editing.
- Review.
- Approval.
- Release preparation.
- Published.

### Production information

- Recording date.
- Location.
- Producer.
- Editor.
- Presenter.
- Assigned crew.
- Editing deadline.
- Review deadline.
- Approval deadline.

### Distribution panel

Show each channel independently.

Example:

| Channel | Scheduled | Status |
|---|---|---|
| YouTube | 1 Dec | Scheduled |
| Spotify | 1 Dec | Published |
| Television | 3 Dec | Awaiting approval |

### Activity history

Show:

- Status changes.
- Date changes.
- Assignments.
- Comments or notes.
- Publication confirmations.

---

## 6.4 Release screen

Display content in release-focused views:

- All releases.
- Ready to publish.
- Scheduled.
- Published.
- Postponed.
- Overdue.

Provide a clear distinction between content production completion and distribution completion.

---

## 6.5 Task screen

Views:

- My Tasks.
- All Tasks.
- Assigned to Me.
- Assigned by Me.
- Overdue.
- Completed.

Provide list and board views.

Allow task completion directly from the list.

---

## 6.6 Reminder centre

Display:

- Upcoming reminders.
- Delivered reminders.
- Failed reminders.
- Cancelled reminders.
- Notification preferences.

Users should be able to choose their preferred notification channels.

---

## 6.7 Settings screen

Include:

- User profile.
- Password.
- Theme.
- Timezone.
- Notification preferences.
- Default calendar view.
- Default reminder offsets.
- Team management.
- Role management.
- System configuration.

---

# PART 7 — BACKGROUND JOBS AND NOTIFICATION LOGIC

## 7. Reminder engine

Use Hangfire with persistent PostgreSQL storage.

Implement the following background processes.

### Job 1: Reminder dispatcher

Runs continuously or on a short polling interval.

Responsibilities:

- Identify due reminders.
- Acquire a processing lock.
- Create notification records.
- Deliver notifications.
- Record delivery status.
- Retry failures.

### Job 2: Overdue detection

Run periodically.

Identify:

- Overdue tasks.
- Overdue milestones.
- Releases past their scheduled time without publication confirmation.

Do not repeatedly create duplicate overdue notifications.

### Job 3: Reminder reconciliation

Run whenever a source record changes its date.

Responsibilities:

- Cancel obsolete pending reminders.
- Recalculate future reminders.
- Preserve delivered reminders.
- Create new pending reminders.

### Job 4: Notification escalation

Allow configurable escalation policies.

Example:

- Notify task owner when overdue.
- Notify production manager after a configured period.
- Record escalation in the activity log.

### Job 5: Database maintenance

Schedule routine maintenance and monitoring tasks.

Use automated backups separately from Hangfire.

---

# PART 8 — SECURITY AND DATA INTEGRITY

## 8. Authentication

Implement:

- Individual user accounts.
- Secure password hashing.
- Access token expiry.
- Refresh token rotation.
- Refresh token revocation.
- Account activation and deactivation.
- Password change.
- Rate limiting on authentication endpoints.

Use secure storage for desktop credentials:

- macOS Keychain.
- Windows Credential Manager.

Do not store access tokens in plain-text configuration files.

## 8.1 Authorisation

Enforce permissions on the backend.

Hiding a button in the desktop interface is not sufficient security.

Every protected API operation must verify the user's permission.

## 8.2 Concurrency

Multiple users may edit the same project or episode.

Use optimistic concurrency.

When a user attempts to save an outdated version:

- Reject the conflicting update.
- Explain that another user has modified the record.
- Offer to reload the latest version.
- Preserve unsaved user input where possible.

## 8.3 Audit trail

Record important changes.

Do not allow normal users to modify or delete audit history.

## 8.4 Backups

Implement:

- Automated PostgreSQL backups.
- Encrypted backup storage.
- Backup retention policies.
- Documented restoration procedures.
- Regular restoration tests.

A backup that has never been tested is not a reliable recovery strategy.

---

# PART 9 — PERFORMANCE AND QUALITY REQUIREMENTS

## 9. Performance targets

Under normal network and server conditions:

| Operation | Target |
|---|---|
| Dashboard loading | Under 2 seconds |
| Calendar loading | Under 2 seconds |
| Task listing | Under 1.5 seconds |
| Episode details | Under 2 seconds |
| Search | Under 1.5 seconds |
| Simple save operation | Under 1 second server processing |

These are initial targets, not guarantees independent of hardware and network conditions.

Use:

- Database indexes.
- Pagination.
- Asynchronous API calls.
- Efficient EF Core queries.
- DTO projections.
- Server-side filtering.
- Caching for suitable reference data.

Avoid unnecessary database queries inside loops.

Use `AsNoTracking()` for read-only EF Core queries.

Do not load entire tables to filter them in the desktop application.

## 9.1 Testing

Create automated tests for:

- Authentication.
- Permission enforcement.
- Project creation.
- Task assignment.
- Episode lifecycle.
- Multiple release schedules.
- Reminder scheduling.
- Reminder cancellation.
- Event rescheduling.
- Overdue detection.
- Concurrent edits.
- Calendar aggregation.
- Timezone conversion.
- Notification retry logic.

Test against PostgreSQL, not only EF Core's in-memory provider.

---

# PART 10 — DEVELOPMENT PHASES

## 10. Implementation sequence

Claude Code must follow this order.

### Phase 0: Project foundation

Deliver:

- Solution and project structure.
- Dependency injection.
- Configuration.
- Logging.
- PostgreSQL connection.
- EF Core setup.
- Initial migrations.
- Health checks.
- API documentation.

Acceptance criteria:

- Solution builds.
- API starts.
- Database connects.
- Initial migrations apply.
- Health endpoint works.

### Phase 1: Authentication and users

Deliver:

- Login.
- Token handling.
- User management.
- Roles.
- Permissions.
- Secure desktop session management.

Acceptance criteria:

- Users can log in.
- Invalid credentials are rejected.
- Permissions are enforced by the API.
- Users can securely log out.

### Phase 2: Desktop foundation

Deliver:

- Avalonia desktop shell.
- Sidebar.
- Top navigation.
- Theme support.
- API client.
- Navigation system.
- Error handling.
- Loading states.

Acceptance criteria:

- Desktop app launches on macOS.
- Desktop app connects to the API.
- Login works.
- Navigation works.
- Connection state is visible.

### Phase 3: Dashboard and calendar

Deliver:

- Personal dashboard.
- Management dashboard.
- Calendar month/week/day/agenda.
- Standalone events.
- Calendar aggregation.
- Filters.
- Quick-create actions.

Acceptance criteria:

- Events appear in the correct calendar views.
- Source records are represented without duplication.
- Calendar items open their original records.

### Phase 4: Tasks and projects

Deliver:

- Project creation.
- Project members.
- Milestones.
- Task creation.
- Assignments.
- Task status changes.
- Project timelines.

Acceptance criteria:

- Assigned tasks appear in personal dashboards.
- Milestones appear on the calendar.
- Completed records preserve history.

### Phase 5: Shows and episodes

Deliver:

- Show management.
- Seasons.
- Episodes.
- Recording schedules.
- Production assignments.
- Editing and review deadlines.
- Episode lifecycle.

Acceptance criteria:

- A show can have multiple seasons.
- Seasons can have multiple episodes.
- Episodes can have different producers and editors.
- Recording dates appear in the calendar.

### Phase 6: Release management

Deliver:

- Distribution channels.
- Multiple releases per episode.
- Scheduling.
- Publication confirmation.
- Postponement.
- Cancellation.
- Overdue release detection.

Acceptance criteria:

- One episode can have independent release dates across channels.
- Published episodes preserve actual publication timestamps.
- Missed releases are flagged.
- Rescheduled releases update their reminders.

### Phase 7: Reminder engine

Deliver:

- Configurable reminder offsets.
- Background jobs.
- In-app notifications.
- Desktop notifications.
- Email notifications.
- Delivery history.
- Retries and duplicate prevention.

Acceptance criteria:

- Reminders work when desktop clients are closed.
- A changed source date recalculates pending reminders.
- Failed notifications are recorded.
- Duplicate notifications are prevented.

### Phase 8: Reports and refinement

Deliver:

- Management reports.
- Workload reports.
- Publication reports.
- CSV export.
- Performance optimisation.
- User acceptance testing.
- Production deployment.

---

# PART 11 — CLAUDE CODE DEVELOPMENT INSTRUCTIONS

## 11. Master development prompt

The following instructions must govern all development work.

### Project identity

You are the lead software architect and senior .NET engineer responsible for building DOF Production Manager.

This is a completely new software project.

Do not reference, import, reuse or depend on the architecture, database, files or code from any previous DOF application.

Build a standalone cross-platform desktop application with a cloud-hosted ASP.NET Core backend.

### Engineering principles

1. Use clean architecture with clear dependency boundaries.
2. Use a modular monolith rather than microservices.
3. Use PostgreSQL as the authoritative database.
4. Use Entity Framework Core and Npgsql.
5. Use Avalonia UI with MVVM.
6. Use ASP.NET Core for the API.
7. Use Hangfire for persistent background processing.
8. Use SignalR for real-time notifications.
9. Use dependency injection.
10. Use asynchronous operations.
11. Validate all incoming requests.
12. Enforce permissions on the server.
13. Write automated tests.
14. Maintain API documentation.
15. Keep the application modular and maintainable.

### Rules for implementation

- Do not generate the entire application in one pass.
- Do not skip architecture planning.
- Do not create unnecessary abstractions.
- Do not introduce microservices.
- Do not connect the desktop client directly to the database.
- Do not hardcode business rules into UI components.
- Do not use mock data as a replacement for actual backend integration in completed features.
- Do not silently swallow exceptions.
- Do not create duplicate calendar records for dates already owned by other entities.
- Do not treat a scheduled release as a published release.
- Do not implement client-only reminders.
- Do not store passwords or credentials in plain text.
- Do not use destructive database migrations without explicit review.
- Do not change existing database relationships without updating documentation and tests.

### Required development workflow

For each phase:

1. Read the relevant technical specification.
2. Inspect the existing solution.
3. Identify the files and modules affected.
4. Propose an implementation plan.
5. Implement the smallest complete feature.
6. Build the solution.
7. Run the relevant tests.
8. Resolve build and test failures.
9. Update technical documentation.
10. Report what was completed and what remains.

Never claim that a feature is complete if it has only been implemented visually.

### Quality gates

A phase cannot be considered complete unless:

- The code compiles.
- Relevant automated tests pass.
- API validation works.
- Permissions are enforced.
- Database changes are migrated.
- Error states are handled.
- Documentation is updated.

### Initial instruction

Begin with Phase 0 only.

Before writing code, inspect the development environment and confirm:

- Installed .NET SDK.
- Avalonia tooling availability.
- PostgreSQL availability.
- Docker availability.
- Git availability.
- Target operating system.
- Required workload and package dependencies.

Produce a setup report.

Then create the solution structure, configure the backend, establish PostgreSQL connectivity, configure EF Core migrations and add health checks.

Do not begin Phase 1 until Phase 0 has passed its acceptance criteria.

---

# PART 12 — DEFINITION OF DONE

The first production-ready release must allow DOF to:

1. Create and manage team accounts.
2. See all organisational activities in one calendar.
3. Assign responsibilities to individual people.
4. Create and track projects.
5. Manage recurring shows and episodes.
6. Record when an episode was produced.
7. Schedule when it will air or be published.
8. Schedule independent releases across different platforms.
9. Receive automated reminders.
10. Identify overdue projects, tasks and releases.
11. Confirm when content actually goes live.
12. View historical production activity.
13. Access shared information from multiple computers.

The system must operate without depending on the old DOF Hub.

---

# FINAL ENGINEERING DIRECTIVE

Build DOF Production Manager as an operational coordination system, not simply a calendar application.

The calendar is the central visualisation layer.

The underlying system must maintain projects, shows, episodes, releases, tasks, assignments and reminders as independently managed but interconnected records.

Prioritise reliability, clarity, accountability and maintainability over feature quantity.

**The ultimate measure of success is that DOF management can open the application at any time and understand what the organisation is working on, what is due next, who is responsible, what is delayed and what content is going live.**