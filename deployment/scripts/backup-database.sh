#!/usr/bin/env bash
# Encrypted PostgreSQL backup with retention (spec 8.4).
#
#   DOF_DB_URL="postgresql://dof_pm:...@localhost:5432/dof_production_manager" \
#   DOF_BACKUP_DIR=/var/backups/dof \
#   DOF_BACKUP_PASSPHRASE_FILE=/etc/dof/backup-passphrase \
#   DOF_BACKUP_RETENTION_DAYS=30 \
#   deployment/scripts/backup-database.sh
#
# Produces <dir>/dof-<UTC timestamp>.dump.enc (pg_dump custom format, AES-256 encrypted with a key
# derived from the passphrase via PBKDF2) plus a .sha256 checksum, then deletes backups older than the
# retention period. Keep the passphrase file outside the backup location (e.g. in a password manager
# and on the server with chmod 600). Schedule with cron or a systemd timer, e.g. daily at 02:00:
#   0 2 * * * /opt/dof/deployment/scripts/backup-database.sh >> /var/log/dof-backup.log 2>&1
set -euo pipefail

: "${DOF_DB_URL:?Set DOF_DB_URL to the database connection URL}"
: "${DOF_BACKUP_DIR:?Set DOF_BACKUP_DIR to the backup directory}"
: "${DOF_BACKUP_PASSPHRASE_FILE:?Set DOF_BACKUP_PASSPHRASE_FILE to a file holding the encryption passphrase}"
RETENTION_DAYS="${DOF_BACKUP_RETENTION_DAYS:-30}"

[[ -r "$DOF_BACKUP_PASSPHRASE_FILE" ]] || { echo "Passphrase file not readable: $DOF_BACKUP_PASSPHRASE_FILE" >&2; exit 1; }
mkdir -p "$DOF_BACKUP_DIR"
umask 077

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
target="$DOF_BACKUP_DIR/dof-$stamp.dump.enc"
partial="$target.partial"
trap 'rm -f "$partial"' EXIT

pg_dump --format=custom --no-owner --no-privileges --dbname="$DOF_DB_URL" \
  | openssl enc -aes-256-cbc -salt -pbkdf2 -iter 200000 -pass "file:$DOF_BACKUP_PASSPHRASE_FILE" -out "$partial"
mv "$partial" "$target"
( cd "$DOF_BACKUP_DIR" && shasum -a 256 "$(basename "$target")" > "$(basename "$target").sha256" )
echo "$(date -u +%FT%TZ) backup written: $target ($(du -h "$target" | cut -f1))"

# Retention: remove encrypted backups (and their checksums) older than RETENTION_DAYS.
find "$DOF_BACKUP_DIR" -name 'dof-*.dump.enc' -mtime "+$RETENTION_DAYS" -print -delete
find "$DOF_BACKUP_DIR" -name 'dof-*.dump.enc.sha256' -mtime "+$RETENTION_DAYS" -delete
