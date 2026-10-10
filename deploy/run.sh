#!/bin/sh
# docker compose for the production stack with the right files, from any directory:
#   ./deploy/run.sh up -d --build      start or update
#   ./deploy/run.sh ps                 state of the containers
#   ./deploy/run.sh logs -f app        follow the application log
set -e
root=$(cd "$(dirname "$0")/.." && pwd)
if [ ! -f "$root/.env" ]; then
    echo "No $root/.env: copy .env.example to .env and fill it in" >&2
    exit 1
fi
exec docker compose -f "$root/deploy/docker-compose.yml" --env-file "$root/.env" "$@"
