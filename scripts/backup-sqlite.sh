#!/usr/bin/env bash
set -euo pipefail

# OwnPlanner SQLite backup script (Docker + Ubuntu)
# - Creates consistent SQLite backups using VACUUM INTO
# - Copies backups to a local directory on the host
# - Applies simple retention cleanup
#
# Requirements on host:
#   - docker
#   - bash
# Optional:
#   - gzip (for compression)
#
# Usage examples:
#   ./scripts/backup-sqlite.sh
#   BACKUP_DIR=/var/backups/ownplanner ./scripts/backup-sqlite.sh
#   CONTAINER=ownplanner RETENTION_DAYS=14 ./scripts/backup-sqlite.sh

CONTAINER="${CONTAINER:-ownplanner}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/ownplanner}"
RETENTION_DAYS="${RETENTION_DAYS:-30}"
COMPRESS="${COMPRESS:-1}" # 1=true, 0=false

DATA_DIR_IN_CONTAINER="${DATA_DIR_IN_CONTAINER:-/app/data}"
AUTH_DB_IN_CONTAINER="${AUTH_DB_IN_CONTAINER:-/app/data/auth/auth.db}"
USER_DB_GLOB_IN_CONTAINER="${USER_DB_GLOB_IN_CONTAINER:-/app/data/databases/ownplanner-user-*.db}"

TS="$(date -u +%Y%m%dT%H%M%SZ)"
HOST_RUN_DIR="${BACKUP_DIR}/runs/${TS}"

if [[ $EUID -ne 0 ]]; then
  echo "This script writes to ${BACKUP_DIR}. Run as root (or set BACKUP_DIR to a writable path)." >&2
  exit 1
fi

if ! command -v docker >/dev/null 2>&1; then
  echo "docker is required" >&2
  exit 1
fi

mkdir -p "${HOST_RUN_DIR}"

# Create a temp dir inside the mounted /app/data so we can docker cp it back out.
TMP_DIR_IN_CONTAINER="${DATA_DIR_IN_CONTAINER}/backups-tmp/${TS}"

echo "Creating backups in container '${CONTAINER}' at '${TMP_DIR_IN_CONTAINER}'..."

docker exec "${CONTAINER}" sh -lc "
  set -euo pipefail
  mkdir -p '${TMP_DIR_IN_CONTAINER}'

  if command -v sqlite3 >/dev/null 2>&1; then
    SQLITE3=sqlite3
  else
    echo 'sqlite3 not found in container. Install sqlite3 in the runtime image or use a sidecar that has it.' >&2
    exit 1
  fi

  # Auth DB
  if [ -f '${AUTH_DB_IN_CONTAINER}' ]; then
    "\$SQLITE3" '${AUTH_DB_IN_CONTAINER}' \"VACUUM INTO '${TMP_DIR_IN_CONTAINER}/auth-${TS}.db'\";
  else
    echo 'Auth DB not found at ${AUTH_DB_IN_CONTAINER}' >&2
  fi

  # Per-user DBs
  for f in ${USER_DB_GLOB_IN_CONTAINER}; do
    [ -e "\$f" ] || continue
    base=\"$(basename \"\$f\" .db)\"
    "\$SQLITE3" "\$f" \"VACUUM INTO '${TMP_DIR_IN_CONTAINER}/\${base}-${TS}.db'\";
  done

  # Record manifest
  (cd '${TMP_DIR_IN_CONTAINER}' && ls -lah > manifest.txt)
" 

echo "Copying backups to host '${HOST_RUN_DIR}'..."
docker cp "${CONTAINER}:${TMP_DIR_IN_CONTAINER}/." "${HOST_RUN_DIR}/"

# Cleanup temp files in container
docker exec "${CONTAINER}" sh -lc "rm -rf '${TMP_DIR_IN_CONTAINER}'" >/dev/null

if [[ "${COMPRESS}" == "1" ]]; then
  if command -v gzip >/dev/null 2>&1; then
    echo "Compressing backups..."
    find "${HOST_RUN_DIR}" -maxdepth 1 -type f -name "*.db" -print0 | xargs -0 -r gzip -9
  else
    echo "gzip not found on host; skipping compression" >&2
  fi
fi

echo "Writing checksums..."
(
  cd "${HOST_RUN_DIR}"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum * 2>/dev/null > SHA256SUMS.txt || true
  fi
)

echo "Applying retention (delete run dirs older than ${RETENTION_DAYS} days)..."
find "${BACKUP_DIR}/runs" -mindepth 1 -maxdepth 1 -type d -mtime "+${RETENTION_DAYS}" -print0 2>/dev/null | xargs -0 -r rm -rf

echo "Backup complete: ${HOST_RUN_DIR}"