#!/usr/bin/env bash
# Builds self-contained desktop packages (no .NET install needed on staff computers).
#   deployment/scripts/publish-desktop.sh https://dof-api.example.org/
# The API address is written into each package's appsettings.json.
set -euo pipefail
api_url="${1:?Usage: publish-desktop.sh <API base URL, ending with />}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
out="$root/artifacts/desktop"
rm -rf "$out"

for rid in osx-arm64 osx-x64 win-x64; do
  dotnet publish "$root/src/DOF.Desktop/DOF.Desktop.csproj" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=false -o "$out/$rid"
  python3 - "$out/$rid/appsettings.json" "$api_url" <<'PY'
import json, sys
path, url = sys.argv[1], sys.argv[2]
with open(path) as f:
    settings = json.load(f)
settings["ApiBaseUrl"] = url
with open(path, "w") as f:
    json.dump(settings, f, indent=2)
PY
  echo "Packaged $rid -> $out/$rid"
done
