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
