#!/usr/bin/env bash
# Creates the local development and test databases for DOF Production Manager.
# Idempotent: safe to run more than once. Requires a local PostgreSQL superuser session (psql).
set -euo pipefail

ROLE="${DOF_DB_ROLE:-dof_pm}"
DATABASES=("dof_production_manager_dev" "dof_production_manager_test")

psql -d postgres -v ON_ERROR_STOP=1 -tAc "SELECT 1 FROM pg_roles WHERE rolname = '${ROLE}'" | grep -q 1 \
  || psql -d postgres -v ON_ERROR_STOP=1 -c "CREATE ROLE ${ROLE} LOGIN"

for db in "${DATABASES[@]}"; do
  psql -d postgres -v ON_ERROR_STOP=1 -tAc "SELECT 1 FROM pg_database WHERE datname = '${db}'" | grep -q 1 \
    || psql -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE ${db} OWNER ${ROLE} ENCODING 'UTF8'"
done

echo "Role '${ROLE}' and databases ready: ${DATABASES[*]}"
