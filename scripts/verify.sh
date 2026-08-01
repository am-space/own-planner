#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
FRONTEND_DIR="$REPO_ROOT/OwnPlanner.Web/ownplanner.web.client"

usage() {
  cat <<'EOF'
Usage: scripts/verify.sh [--all|--backend|--frontend]

  --all       Verify the frontend and backend (default).
  --backend   Build and test the .NET solution.
  --frontend  Lint and build the frontend.

Run scripts/setup.sh first to install and restore dependencies.
EOF
}

selection="all"

if (( $# > 1 )); then
  usage >&2
  exit 2
fi

if (( $# == 1 )); then
  case "$1" in
    --all)
      selection="all"
      ;;
    --backend)
      selection="backend"
      ;;
    --frontend)
      selection="frontend"
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
fi

verify_frontend() {
  echo "Linting frontend..."
  npm --prefix "$FRONTEND_DIR" run lint

  echo "Building frontend..."
  npm --prefix "$FRONTEND_DIR" run build
}

verify_backend() {
  echo "Building .NET solution..."
  dotnet build "$REPO_ROOT/OwnPlanner.sln" --no-restore -c Release --disable-build-servers -m:1

  echo "Testing .NET solution..."
  dotnet test "$REPO_ROOT/OwnPlanner.sln" --no-build -c Release --verbosity normal
}

case "$selection" in
  all)
    verify_frontend
    verify_backend
    ;;
  frontend)
    verify_frontend
    ;;
  backend)
    verify_backend
    ;;
esac
