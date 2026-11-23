# Deployment and Operations

How to run DOF Production Manager in production (spec 2.3: desktop apps on each computer, API and
PostgreSQL on a server everyone reaches), back it up and restore it (spec 8.4), and upgrade it.

## What runs where

| Part | Where | Notes |
|---|---|---|
| PostgreSQL 17 | Server (container or managed service) | Never exposed publicly; only the API connects |
| API (`DOF.Api`) | Server, behind a TLS reverse proxy | Also runs the reminder jobs (Hangfire, schema `hangfire` in the same database) |
| Desktop app | Each staff computer (macOS, Windows) | Talks only to the API over HTTPS; stores its refresh token in the Keychain / Credential Manager |

## Server setup (single server with Docker)

Files: `deployment/docker/Dockerfile.api`, `deployment/docker/docker-compose.production.yml`,
`deployment/docker/.env.example`.

1. Install Docker Engine and Docker Compose on a Linux server, and a reverse proxy with automatic TLS
   (Caddy is simplest: `dof-api.example.org { reverse_proxy 127.0.0.1:8080 }`).
2. Copy the repository (or a release archive) to the server, e.g. `/opt/dof`.
3. `cp deployment/docker/.env.example deployment/docker/.env` and fill it in:
   long random values for `POSTGRES_PASSWORD` and `JWT_SIGNING_KEY` (`openssl rand -base64 48`), the
   organisation timezone, the first administrator's e-mail and password, and optionally the SMTP
   settings. `.env` is git-ignored; keep a copy in the organisation's password manager.
4. Start the database: `docker compose -f deployment/docker/docker-compose.production.yml --env-file deployment/docker/.env up -d db`.
5. Apply the migrations (see *Database migrations* below).
6. Start the API: `docker compose … up -d --build api`. On first start it seeds roles, permissions,
   distribution channels and the bootstrap administrator, and registers the recurring reminder jobs.
7. Check `https://dof-api.example.org/health` returns `"Healthy"`, sign in from a desktop app, then
   remove `BOOTSTRAP_ADMIN_PASSWORD` from `.env` (it is only used when the user table is empty).

Without Docker, the same settings work as environment variables for `dotnet DOF.Api.dll` (published
with `dotnet publish src/DOF.Api -c Release`), run under systemd behind the reverse proxy.

> Verified locally: the Release build starts in Production mode from environment variables only,
> reports healthy, does not expose Swagger, and rejects anonymous requests. The container files were
> written for Docker but **not built here** (Docker is not installed on the development machine). Build
> and run them once on the target server before go-live.

## Configuration reference

| Setting (env var) | Purpose |
|---|---|
| `ConnectionStrings__Database` | PostgreSQL connection string (with password) |
| `Jwt__SigningKey` | ≥ 32 bytes, secret. Changing it signs everyone out |
| `Organisation__DefaultTimeZone` | IANA zone, e.g. `Africa/Nairobi` |
| `Bootstrap__AdminEmail` / `__AdminPassword` | First administrator, used only on an empty database |
| `Database__ApplyMigrationsOnStartup` | Keep `false` in production; run migrations explicitly |
| `BackgroundJobs__Enabled` / `__RunServer` | Reminder jobs on (default). With several API instances, all may run the server: claims use row locks |
| `Reminders__*` | Delivery attempts (5), escalation after hours (24), date-only reminder hour (9), reconcile horizon (60 days), outbox retention (30 days) |
| `Email__Host` / `__Port` / `__Username` / `__Password` / `__FromAddress` | SMTP. Without a host, e-mail is shown as unavailable |
| `RateLimiting__AuthPermitLimitPerMinute` | Sign-in attempts per client per minute (10) |
| `GoogleCalendar__ClientId` / `__ClientSecret` | Google OAuth client for calendar invites (see *Google Calendar* below) |
| `GoogleCalendar__TokenEncryptionKey` | `openssl rand -base64 32`. Encrypts the stored Google refresh token. Changing it means connecting Google again |

Secrets come only from the environment or a secret store, never from committed files.

## Database migrations

Migrations are reviewed in code and are additive (see `DATABASE_SCHEMA.md`). Apply them explicitly:

```bash
deployment/scripts/build-migrations-bundle.sh linux-x64     # on a build machine with the SDK
# copy artifacts/efbundle-linux-x64 to the server, then (API stopped, backup taken):
./efbundle-linux-x64 --connection "Host=127.0.0.1;Port=5432;Database=dof_production_manager;Username=dof_pm;Password=…"
```

(When PostgreSQL runs in Docker, either publish its port to 127.0.0.1 temporarily or run the bundle in
a container on the compose network.) The bundle is idempotent: it applies only pending migrations.

## Backups (spec 8.4)

Scripts in `deployment/scripts/`:

| Script | What it does |
|---|---|
| `backup-database.sh` | `pg_dump` (custom format), encrypted with AES-256 (OpenSSL, PBKDF2 key from a passphrase file), plus a SHA-256 checksum; deletes backups older than `DOF_BACKUP_RETENTION_DAYS` (30) |
| `restore-database.sh` | Verifies the checksum, decrypts and restores into a given (empty) database |
| `test-restore.sh` | Restores the latest backup into a scratch database, checks migrations and core tables, drops it |

Recommended schedule (cron on the server):

```
0 2 * * *  DOF_DB_URL=… DOF_BACKUP_DIR=/var/backups/dof DOF_BACKUP_PASSPHRASE_FILE=/etc/dof/backup-passphrase /opt/dof/deployment/scripts/backup-database.sh >> /var/log/dof-backup.log 2>&1
30 3 * * 0 DOF_DB_ADMIN_URL=… DOF_BACKUP_DIR=/var/backups/dof DOF_BACKUP_PASSPHRASE_FILE=/etc/dof/backup-passphrase /opt/dof/deployment/scripts/test-restore.sh >> /var/log/dof-backup.log 2>&1
```

- Copy `/var/backups/dof` off the server daily (object storage or another site). The files are
  encrypted, so the storage does not need to be trusted.
- Keep the passphrase in the password manager **and** on the server (`chmod 600`), never next to the
  backups. Without it, backups cannot be restored.
- `test-restore.sh` needs a role that can create databases (`DOF_DB_ADMIN_URL`), not the app role.
- Hangfire's job history is in the same database and is restored with it.

> Verified locally: a backup of the development database was encrypted, checksummed, restored into a
> scratch database by `test-restore.sh` (7 migrations, users present, core tables readable), and the
> scratch database was removed. A wrong passphrase makes the restore fail.

### Restoring after an incident

1. Stop the API (`docker compose … stop api`).
2. Create a new empty database and restore the chosen backup into it with `restore-database.sh`.
3. Check it (counts, latest records). `test-restore.sh` shows the checks.
4. Point `ConnectionStrings__Database` at the restored database (or rename databases), start the API,
   check `/health`, and sign in.
5. Record the incident and the restore in the operations log.

## Google Calendar

DOF sends Google Calendar invites from one Google account that an administrator connects in
**Settings → Google Calendar**. Setting this up once:

1. In the [Google Cloud console](https://console.cloud.google.com/), create a project (for example
   "DOF Production Manager") and enable the **Google Calendar API** for it.
2. Under **Google Auth Platform → Branding / Audience**, set up the consent screen: app name, support
   e-mail, audience **External** (or **Internal** for a Google Workspace domain). Add the scope
   `https://www.googleapis.com/auth/calendar.events`.
3. While the app is in **Testing**, add the account that will send invites as a test user. Google
   expires refresh tokens of Testing apps after 7 days, so **publish the app (In production)** once it
   works. An unverified app shows a warning on the sign-in screen ("Google hasn't verified this app");
   the administrator chooses *Advanced → Continue*. Only the connecting account sees this screen, and
   guests never do.
4. Under **Clients**, create an OAuth client of type **Desktop app**. Put its client ID and client
   secret in `GoogleCalendar__ClientId` / `GoogleCalendar__ClientSecret`, generate
   `GoogleCalendar__TokenEncryptionKey`, and restart the API.
5. In the desktop app, open **Settings → Google Calendar → Connect Google account…**, sign in to the
   sending account in the browser and allow access. Settings then shows "Invites are sent from …".

Guests receive Google's normal invitation e-mail and the event appears in their Google Calendar.
Editing the event in DOF updates their calendars within a minute, and cancelling it there removes it.
Disconnecting stops updates; events already sent stay on guests' calendars.

## Schedule PDFs

The Calendar page's **PDF** menu downloads the day, week, month or year in view (with the active
filters). The desktop's save dialog starts in `ExportFolder` from the desktop `appsettings.json`
(default: the user's Documents folder). PDFs are drawn by QuestPDF under its Community licence, which
is free for qualifying non-profit organisations and organisations with under USD 1M annual revenue.
Check that DOF qualifies; otherwise a paid licence is needed.

## Desktop installation

`deployment/scripts/publish-desktop.sh https://dof-api.example.org/` builds self-contained packages
for `osx-arm64`, `osx-x64` and `win-x64` under `artifacts/desktop/` (no .NET install needed), with the
API address written into each package's `appsettings.json`. Distribute the folder (zip) to staff. For
smooth installs, sign and notarise the macOS app and sign the Windows executable with the
organisation's certificates (not automated here).

## Monitoring and upgrades

- Monitor `GET /health/live` (process) and `GET /health` (database) every minute and alert on failure.
- Logs go to standard output (Serilog). Collect them with the container runtime or journald. Failed
  e-mails and jobs log at Error level.
- Upgrade: take a backup → build and apply the new migrations bundle → `docker compose … up -d --build api`
  → check `/health` → publish new desktop packages if the client changed.
