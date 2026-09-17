#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DATABASE_PATH="$PROJECT_ROOT/MyApp/App_Data/app.db"
FILES_PATH="$PROJECT_ROOT/MyApp/App_Data/files"
LOCAL_SETTINGS="$PROJECT_ROOT/MyApp/appsettings.Development.json"

# shellcheck disable=SC1091
. "$SCRIPT_DIR/load-env.sh"
load_env_file "$PROJECT_ROOT/.env"

# The local database provider is whatever the developer runs, which is normally whatever is
# deployed. Database__Provider in the private .env wins, then the Development default.
PROVIDER="${Database__Provider:-${DB_PROVIDER:-sqlite}}"
if [[ -z "${Database__Provider:-}" && -f "$LOCAL_SETTINGS" ]] && command -v node >/dev/null 2>&1; then
  LOCAL_PROVIDER="$(node -p "JSON.parse(require('fs').readFileSync(process.argv[1],'utf8')).Database?.Provider ?? ''" "$LOCAL_SETTINGS" 2>/dev/null || true)"
  if [[ -n "$LOCAL_PROVIDER" ]]; then
    PROVIDER="$LOCAL_PROVIDER"
  fi
fi
case "$(printf '%s' "$PROVIDER" | tr '[:upper:]' '[:lower:]')" in
  sqlite) PROVIDER="sqlite" ;;
  postgres|postgresql) PROVIDER="postgres" ;;
  sqlserver|mssql) PROVIDER="sqlserver" ;;
  mysql|mariadb) PROVIDER="mysql" ;;
  *) printf 'Unsupported database provider: %s\n' "$PROVIDER" >&2; exit 2 ;;
esac

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

if [[ "$PROVIDER" == "sqlite" ]]; then
  rm -f "$DATABASE_PATH" "$DATABASE_PATH-shm" "$DATABASE_PATH-wal"
else
  # A server provider keeps its data in a container volume, so recreate that instead.
  "$SCRIPT_DIR/dev-db.sh" reset --provider "$PROVIDER"
fi
rm -rf "$FILES_PATH"
mkdir -p "$PROJECT_ROOT/MyApp/App_Data"

cd "$PROJECT_ROOT/MyApp"
dotnet run --no-build --no-launch-profile --AppTasks=migrate
echo "Development $PROVIDER database, users, plans, and local file store recreated."
