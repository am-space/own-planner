#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
PROJECT="$REPO_ROOT/OwnPlanner.Deployment.Tests/OwnPlanner.Deployment.Tests.csproj"

cleanup() {
  local exit_code=$?
  if (( exit_code != 0 )); then
    docker compose --project-directory "$REPO_ROOT" logs --no-color app > "$REPO_ROOT/TestResults/deployment-container.log" 2>/dev/null || true
  fi
  "$SCRIPT_DIR/docker-down.sh" --volumes || true
  return "$exit_code"
}
trap cleanup EXIT

mkdir -p "$REPO_ROOT/TestResults"
"$SCRIPT_DIR/docker-up.sh" smoke

OWNPLANNER_BASE_URL="http://127.0.0.1:${OWNPLANNER_PORT:-8080}" \
  dotnet test "$PROJECT" --filter "Category=DeploymentSmoke" \
  --logger "trx;LogFileName=deployment-smoke.trx" \
  --results-directory "$REPO_ROOT/TestResults/Deployment"

