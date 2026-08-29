#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
CONFIGURATION="${PLAYWRIGHT_CONFIGURATION:-Debug}"

install_arguments=(install chromium)
if [[ "${CI:-}" == "true" && "$(uname -s)" == "Linux" ]]; then
  install_arguments=(install --with-deps chromium)
fi

install_for_project() {
  local project_directory="$1"
  local project="$REPO_ROOT/$project_directory/$project_directory.csproj"
  local playwright_root="$REPO_ROOT/$project_directory/bin/$CONFIGURATION/net10.0/.playwright"

  dotnet build "$project" --no-restore -c "$CONFIGURATION" --disable-build-servers -m:1

  if [[ ! -d "$playwright_root/node" || ! -f "$playwright_root/package/cli.js" ]]; then
    echo "Playwright's generated runtime was not found under $playwright_root." >&2
    exit 1
  fi

  local node_binary
  node_binary="$(find "$playwright_root/node" -type f -name node -print -quit)"
  if [[ -z "$node_binary" ]]; then
    echo "Playwright's generated Node executable was not found under $playwright_root/node." >&2
    exit 1
  fi

  "$node_binary" "$playwright_root/package/cli.js" "${install_arguments[@]}"
}

install_for_project "OwnPlanner.E2E.Tests"
install_for_project "OwnPlanner.Deployment.Tests"
