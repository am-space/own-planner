#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
FRONTEND_DIR="$REPO_ROOT/OwnPlanner.Web/ownplanner.web.client"

echo "Installing frontend dependencies..."
npm --prefix "$FRONTEND_DIR" ci

echo "Restoring .NET dependencies..."
dotnet restore "$REPO_ROOT/OwnPlanner.sln"
