#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
MODE="${1:-smoke}"

case "$MODE" in
  smoke)
    ;;
  live-ai)
    if [[ -z "${GEMINI_API_KEY:-}" ]]; then
      echo "GEMINI_API_KEY is required for live-ai mode." >&2
      exit 2
    fi
    ;;
  *)
    echo "Usage: scripts/docker-up.sh [smoke|live-ai]" >&2
    exit 2
    ;;
esac

command -v docker >/dev/null 2>&1 || { echo "docker is required" >&2; exit 1; }
docker compose version >/dev/null

docker compose --project-directory "$REPO_ROOT" up --detach --build --wait app
echo "OwnPlanner is healthy at http://127.0.0.1:${OWNPLANNER_PORT:-8080}"

