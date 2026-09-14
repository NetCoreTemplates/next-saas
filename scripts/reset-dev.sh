#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DATABASE_PATH="$PROJECT_ROOT/MyApp/App_Data/app.db"
FILES_PATH="$PROJECT_ROOT/MyApp/App_Data/files"

if [[ "${ASPNETCORE_ENVIRONMENT:-}" != "Development" ]]; then
  echo "Refusing reset: ASPNETCORE_ENVIRONMENT must be Development." >&2
  exit 2
fi
if [[ "${1:-}" != "--yes" ]]; then
  echo "Usage: ASPNETCORE_ENVIRONMENT=Development ./scripts/reset-dev.sh --yes" >&2
  exit 2
fi
if [[ "$DATABASE_PATH" != "$PROJECT_ROOT/MyApp/App_Data/app.db" || "$FILES_PATH" != "$PROJECT_ROOT/MyApp/App_Data/files" ]]; then
  echo "Refusing reset: target validation failed." >&2
  exit 2
fi

rm -f "$DATABASE_PATH" "$DATABASE_PATH-shm" "$DATABASE_PATH-wal"
rm -rf "$FILES_PATH"
mkdir -p "$PROJECT_ROOT/MyApp/App_Data"

cd "$PROJECT_ROOT/MyApp"
dotnet run --no-build --no-launch-profile --AppTasks=migrate
echo "Development database, users, plans, and local file store recreated."
