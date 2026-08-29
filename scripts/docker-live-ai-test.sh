#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
PROJECT="$REPO_ROOT/OwnPlanner.Deployment.Tests/OwnPlanner.Deployment.Tests.csproj"

if [[ -z "${GEMINI_API_KEY:-}" ]]; then
  echo "GEMINI_API_KEY is required. Export it without placing it in a command argument or tracked file." >&2
  exit 2
fi

export GEMINI_MODEL="${GEMINI_MODEL:-gemini-3.5-flash-lite}"

cleanup() {
  local exit_code=$?
  if (( exit_code != 0 )); then
    docker compose --project-directory "$REPO_ROOT" logs --no-color app > "$REPO_ROOT/TestResults/deployment-live-ai-container.log" 2>/dev/null || true
  fi
  "$SCRIPT_DIR/docker-down.sh" --volumes || true
  return "$exit_code"
}
trap cleanup EXIT

mkdir -p "$REPO_ROOT/TestResults"
"$SCRIPT_DIR/docker-up.sh" live-ai

OWNPLANNER_BASE_URL="http://127.0.0.1:${OWNPLANNER_PORT:-8080}" \
OWNPLANNER_RUN_LIVE_AI=true \
  dotnet test "$PROJECT" --filter "Category=LiveAi" \
  --logger "trx;LogFileName=deployment-live-ai.trx" \
  --results-directory "$REPO_ROOT/TestResults/Deployment"
