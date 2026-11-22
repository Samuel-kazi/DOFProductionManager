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

Status codes: 400 validation, 401 not signed in / bad credentials, 403 missing permission,
404 not found, 409 duplicate or stale `version`, 422 business rule (e.g. last Administrator),
429 rate limit.

Not yet implemented: `GET /users/{id}/workload` (needs tasks, Phase 4).
