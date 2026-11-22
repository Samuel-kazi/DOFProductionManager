#!/usr/bin/env bash
# Restores an encrypted backup into a database (spec 8.4 restoration procedure).
#
#   DOF_BACKUP_PASSPHRASE_FILE=/etc/dof/backup-passphrase \
#   deployment/scripts/restore-database.sh /var/backups/dof/dof-20261122T020000Z.dump.enc \
#       "postgresql://dof_pm:...@localhost:5432/dof_production_manager_restore"
#
# The target database must exist and should be empty (create it with `createdb`). Restoring over the
# live database is deliberately not automated: stop the API, restore into a new database, verify it
# (test-restore.sh does this), then point ConnectionStrings__Database at it and start the API.
set -euo pipefail

backup="${1:?Usage: restore-database.sh <backup.dump.enc> <target database URL>}"
target="${2:?Usage: restore-database.sh <backup.dump.enc> <target database URL>}"
: "${DOF_BACKUP_PASSPHRASE_FILE:?Set DOF_BACKUP_PASSPHRASE_FILE to the file holding the encryption passphrase}"

if [[ -f "$backup.sha256" ]]; then
  ( cd "$(dirname "$backup")" && shasum -a 256 -c "$(basename "$backup").sha256" )
fi

openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -pass "file:$DOF_BACKUP_PASSPHRASE_FILE" -in "$backup" \
  | pg_restore --no-owner --no-privileges --exit-on-error --dbname="$target"
echo "Restored $backup into the target database."
