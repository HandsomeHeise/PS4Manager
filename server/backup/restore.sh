#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
if docker compose ps --status running --services | grep -Eq '^(api|nginx|backup)$'; then
  echo 'Stop api, nginx and backup before restoring. MySQL must remain running.' >&2
  exit 1
fi
exec docker compose -f compose.yaml -f backup/restore.compose.yaml run --rm --no-deps -e RESTORE_WRITERS_STOPPED=1 backup node /app/backup/restore.mjs "$@"
