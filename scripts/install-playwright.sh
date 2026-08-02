#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
E2E_PROJECT="$REPO_ROOT/OwnPlanner.E2E.Tests/OwnPlanner.E2E.Tests.csproj"
CONFIGURATION="${PLAYWRIGHT_CONFIGURATION:-Debug}"
PLAYWRIGHT_ROOT="$REPO_ROOT/OwnPlanner.E2E.Tests/bin/$CONFIGURATION/net10.0/.playwright"

dotnet build "$E2E_PROJECT" --no-restore -c "$CONFIGURATION" --disable-build-servers -m:1

if [[ ! -d "$PLAYWRIGHT_ROOT/node" || ! -f "$PLAYWRIGHT_ROOT/package/cli.js" ]]; then
  echo "Playwright's generated runtime was not found under $PLAYWRIGHT_ROOT." >&2
  exit 1
fi

node_binary="$(find "$PLAYWRIGHT_ROOT/node" -type f -name node -print -quit)"
playwright_cli="$PLAYWRIGHT_ROOT/package/cli.js"

if [[ -z "$node_binary" ]]; then
  echo "Playwright's generated Node executable was not found under $PLAYWRIGHT_ROOT/node." >&2
  exit 1
fi

install_arguments=(install chromium)
if [[ "${CI:-}" == "true" && "$(uname -s)" == "Linux" ]]; then
  install_arguments=(install --with-deps chromium)
fi

"$node_binary" "$playwright_cli" "${install_arguments[@]}"
