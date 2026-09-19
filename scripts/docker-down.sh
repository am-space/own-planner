#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"

case "${1:-}" in
  "")
    docker compose --project-directory "$REPO_ROOT" down --remove-orphans
    ;;
  --volumes)
    docker compose --project-directory "$REPO_ROOT" down --volumes --remove-orphans
    ;;
  *)
    echo "Usage: scripts/docker-down.sh [--volumes]" >&2
    exit 2
    ;;
esac

