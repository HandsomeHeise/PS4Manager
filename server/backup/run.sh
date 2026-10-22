#!/bin/sh
set -eu
if [ "$#" -gt 0 ]; then exec "$@"; fi
while :; do
  /app/backup/backup.sh || echo 'Backup failed; inspect configuration and retry manually.' >&2
  sleep 86400
done
