#!/usr/bin/env bash
# Proves the latest backup can be restored (spec 8.4: "a backup that has never been tested is not a
# reliable recovery strategy"). Restores it into a scratch database on the same server, checks the
# migration history and core tables, then drops the scratch database. Run it weekly from cron.
#
#   DOF_DB_ADMIN_URL="postgresql://dof_pm:...@localhost:5432/postgres" \
#   DOF_BACKUP_DIR=/var/backups/dof DOF_BACKUP_PASSPHRASE_FILE=/etc/dof/backup-passphrase \
#   deployment/scripts/test-restore.sh
set -euo pipefail

: "${DOF_DB_ADMIN_URL:?Set DOF_DB_ADMIN_URL to a URL of a database on the server (used to create the scratch database)}"
: "${DOF_BACKUP_DIR:?Set DOF_BACKUP_DIR}"
: "${DOF_BACKUP_PASSPHRASE_FILE:?Set DOF_BACKUP_PASSPHRASE_FILE}"

latest="$(ls -1t "$DOF_BACKUP_DIR"/dof-*.dump.enc 2>/dev/null | head -n 1 || true)"
[[ -n "$latest" ]] || { echo "No backups found in $DOF_BACKUP_DIR" >&2; exit 1; }

scratch="dof_restore_test_$(date -u +%Y%m%d%H%M%S)"
base="${DOF_DB_ADMIN_URL%/*}"
scratch_url="$base/$scratch"
cleanup() { psql "$DOF_DB_ADMIN_URL" -qc "DROP DATABASE IF EXISTS $scratch" >/dev/null; }
trap cleanup EXIT

psql "$DOF_DB_ADMIN_URL" -qc "CREATE DATABASE $scratch" >/dev/null
"$(dirname "$0")/restore-database.sh" "$latest" "$scratch_url"

migrations="$(psql "$scratch_url" -tAc 'SELECT count(*) FROM "__EFMigrationsHistory"')"
users="$(psql "$scratch_url" -tAc 'SELECT count(*) FROM users')"
latest_migration="$(psql "$scratch_url" -tAc 'SELECT max(migration_id) FROM "__EFMigrationsHistory"')"
[[ "$migrations" -gt 0 && "$users" -gt 0 ]] || { echo "Restore check failed: migrations=$migrations users=$users" >&2; exit 1; }
for table in projects tasks shows episodes releases reminders activity_logs; do
  psql "$scratch_url" -tAc "SELECT 1 FROM $table LIMIT 1" >/dev/null
done
echo "$(date -u +%FT%TZ) restore test passed: $(basename "$latest") -> $migrations migrations (latest $latest_migration), $users users"
