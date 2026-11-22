#!/usr/bin/env bash
# Builds a self-contained EF Core migrations bundle for the server (no SDK needed there).
#   deployment/scripts/build-migrations-bundle.sh linux-x64
# Then on the server, with the API stopped and after a backup:
#   ./efbundle --connection "Host=...;Database=dof_production_manager;Username=...;Password=..."
set -euo pipefail
rid="${1:-linux-x64}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
mkdir -p "$root/artifacts"
cd "$root"
dotnet tool restore
dotnet ef migrations bundle --project src/DOF.Infrastructure --startup-project src/DOF.Api \
  --self-contained -r "$rid" --force -o "artifacts/efbundle-$rid"
echo "Bundle: artifacts/efbundle-$rid"
