#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
FRONTEND_DIR="$REPO_ROOT/OwnPlanner.Web/ownplanner.web.client"

usage() {
  cat <<'EOF'
Usage: scripts/verify.sh [--all|--backend|--frontend|--e2e]

  --all       Verify the frontend, backend, and E2E suite (default).
  --backend   Build and test the .NET solution, excluding browser deployment tests.
  --frontend  Lint and build the frontend.
  --e2e       Build the frontend and run the Playwright E2E suite.

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
    --e2e)
      selection="e2e"
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

  # Run each backend project separately: MTP must fail if any selected project discovers no tests.
  # E2E and deployment-only projects have dedicated entry points and would be empty after filtering.
  local project
  for project in "$REPO_ROOT"/OwnPlanner.*.Tests/*.csproj; do
    case "$(basename "$project")" in
      OwnPlanner.E2E.Tests.csproj|OwnPlanner.Deployment.Tests.csproj) continue ;;
    esac
    echo "Testing $(basename "$project")..."
    dotnet test --project "$project" --no-build --no-restore -c Release \
      --filter-not-trait "Category=E2E" "Category=DeploymentSmoke" "Category=LiveAi"
  done
}

verify_e2e() {
  local build_frontend="${1:-true}"

  if [[ "$build_frontend" == "true" ]]; then
    echo "Building frontend for E2E tests..."
    npm --prefix "$FRONTEND_DIR" run build
  fi

  echo "Building E2E test project..."
  dotnet build "$REPO_ROOT/OwnPlanner.E2E.Tests/OwnPlanner.E2E.Tests.csproj" \
    --no-restore -c Release --disable-build-servers -m:1

  echo "Running E2E tests..."
  dotnet test --project "$REPO_ROOT/OwnPlanner.E2E.Tests/OwnPlanner.E2E.Tests.csproj" \
    --no-build --no-restore -c Release --filter-trait "Category=E2E" \
    --report-xunit-trx --report-xunit-trx-filename e2e.trx --results-directory "$REPO_ROOT/TestResults/E2E"
}

case "$selection" in
  all)
    verify_frontend
    verify_backend
    verify_e2e false
    ;;
  frontend)
    verify_frontend
    ;;
  backend)
    verify_backend
    ;;
  e2e)
    verify_e2e
    ;;
esac
