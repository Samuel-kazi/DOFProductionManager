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

**Calendar filters** (all optional, repeatable where plural): `eventTypes`, `sourceTypes`, `statuses`,
`priorities`, `assignedUserId`, `projectId`, `showId`, `includeCancelled`, and `timeZone` (IANA, for the
day/week/month/agenda boundaries).

**Calendar item:** `{ id, title, sourceType, sourceId, eventType, startAt, endAt, isAllDay, status,
priority, location, assignedUsers[{userId, fullName, assignmentType}], canEdit }`. `sourceType` +
`sourceId` identify the original record, which is where the item must be opened or rescheduled.

Status codes: 400 validation, 401 not signed in / bad credentials, 403 missing permission,
404 not found, 409 duplicate or stale `version`, 422 business rule (e.g. last Administrator),
429 rate limit.

Not yet implemented: `GET /users/{id}/workload` and `GET /dashboard/overdue` (need tasks and milestones, Phase 4).
