#!/usr/bin/env bash
# Runs the deployed database provider locally.
#
# Production selects a provider with a Kamal destination (config/deploy.<provider>.yml).
# This script starts the same image, with the same database name and the same unprivileged
# application login, on the developer's machine, then writes the local connection into the
# private, gitignored .env that the application reads in Development. A developer therefore
# runs exactly what is deployed, DB_PROVIDER in .env is the single switch for both sides, and
# MyApp/appsettings.Development.json keeps the SQLite default every new clone starts from.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$PROJECT_ROOT/.env"
TEMPLATE_SETTINGS="$PROJECT_ROOT/MyApp/appsettings.Development.json"

# config/deploy.yml loads .env through ERB, so honour the same file and the same precedence.
# shellcheck disable=SC1091
. "$SCRIPT_DIR/load-env.sh"
load_env_file "$PROJECT_ROOT/.env"

COMMAND="up"
PROVIDER="${DB_PROVIDER:-sqlite}"
NETWORK="next-saas-dev"
# Local-only credential. Production passwords live in DB_PASSWORD and never in this file;
# this one is deliberately fixed so a teammate can reproduce the same local database.
# It also satisfies SQL Server's password policy, which a hex password does not.
DEV_PASSWORD="${DEV_DB_PASSWORD:-Dev_Passw0rd!Local}"

usage() {
  cat <<'USAGE'
Usage: ./scripts/dev-db.sh [command] [--provider <name>]

Runs the same database provider locally that the deployment uses.

Commands:
  up        Start the local database and write its connection to .env (default).
  down      Stop and remove the container, keeping its data volume.
  reset     Remove the container and its data volume, then start an empty database.
  status    Show the provider, container state, and connection target.
  shell     Open an interactive database client against the local database.
  env       Print export lines for the local provider and connection string.

Options:
  --provider <name>   sqlite, postgres, mysql, or sqlserver (default: $DB_PROVIDER, else sqlite).
  -h, --help          Show this help.

The provider normally comes from DB_PROVIDER in .env, the same variable the Release
workflow passes to Kamal, so local and production stay on one switch. Override the local
password with DEV_DB_PASSWORD, the published port with DEV_DB_PORT, and, when the
destination names a hosting arrangement rather than an engine (a managed instance, say),
the engine to run locally with DEV_DB_ENGINE.

Step-by-step, including the matching production configuration: DATABASE.md.
USAGE
}

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    up|down|reset|status|shell|env) COMMAND="$1" ;;
    --provider)
      shift
      [[ "$#" -gt 0 ]] || { printf 'Missing value after --provider\n' >&2; exit 2; }
      PROVIDER="$1"
      ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'Unknown argument: %s\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done

if [[ ! -f "$PROJECT_ROOT/config/deploy.$PROVIDER.yml" ]]; then
  printf 'Unknown --provider %s. Available providers:\n' "$PROVIDER" >&2
  ls "$PROJECT_ROOT"/config/deploy.*.yml | sed 's|.*/config/deploy\.\(.*\)\.yml|  \1|' >&2
  exit 2
fi

# A destination may describe where a database runs rather than which engine it is, such as a
# managed instance in config/deploy.postgres-managed.yml. DEV_DB_ENGINE then names the engine
# to run locally while DB_PROVIDER keeps selecting the deployment destination.
ENGINE="${DEV_DB_ENGINE:-$PROVIDER}"
CONTAINER="next-saas-dev-$ENGINE"
VOLUME="next-saas-dev-$ENGINE-data"

# Same images as the Kamal accessories in config/deploy.<provider>.yml.
case "$ENGINE" in
  postgres)
    IMAGE="postgres:18-alpine"
    PORT="${DEV_DB_PORT:-5432}"
    CONNECTION="Host=localhost;Port=$PORT;Database=next_saas;Username=next_saas;Password=$DEV_PASSWORD;SSL Mode=Disable"
    SETTINGS_PROVIDER="PostgreSql"
    ;;
  mysql)
    IMAGE="mysql:8.4"
    PORT="${DEV_DB_PORT:-3306}"
    CONNECTION="Server=localhost;Port=$PORT;Database=next_saas;User Id=next_saas;Password=$DEV_PASSWORD;SslMode=Preferred"
    SETTINGS_PROVIDER="MySql"
    ;;
  sqlserver)
    IMAGE="mcr.microsoft.com/mssql/server:2022-latest"
    PORT="${DEV_DB_PORT:-1433}"
    CONNECTION="Server=localhost,$PORT;Database=next_saas;User Id=next_saas;Password=$DEV_PASSWORD;Encrypt=True;TrustServerCertificate=True"
    SETTINGS_PROVIDER="SqlServer"
    ;;
  sqlite)
    IMAGE=""
    PORT=""
    CONNECTION="Data Source=App_Data/app.db;Cache=Shared"
    SETTINGS_PROVIDER="Sqlite"
    ;;
  *)
    printf 'No local database is defined for %s.\n' "$ENGINE" >&2
    printf 'Set DEV_DB_ENGINE to the engine it runs (sqlite, postgres, mysql, or sqlserver),\n' >&2
    printf 'or add its case to scripts/dev-db.sh.\n' >&2
    exit 2
    ;;
esac

DOCKER="${DOCKER:-}"
if [[ -z "$DOCKER" && "$ENGINE" != "sqlite" ]]; then
  for candidate in docker podman; do
    if command -v "$candidate" >/dev/null 2>&1; then
      DOCKER="$candidate"
      break
    fi
  done
  if [[ -z "$DOCKER" ]]; then
    printf 'docker or podman is required to run the %s provider locally.\n' "$ENGINE" >&2
    printf 'Set DB_PROVIDER=sqlite in .env to develop without a database server.\n' >&2
    exit 2
  fi
fi

write_local_settings() {
  # The developer's private .env, never a source-controlled settings file. Keys use the
  # ASP.NET Core environment convention, and Program.cs applies the file in Development.
  if [[ -e "$ENV_FILE" && ! -w "$ENV_FILE" ]]; then
    printf 'Cannot write %s: it is not writable by this user.\n' "$ENV_FILE" >&2
    printf 'A previous run under sudo leaves it owned by root. Take it back with:\n' >&2
    printf '  sudo chown "$USER" %s\n' "$ENV_FILE" >&2
    exit 2
  fi
  set_env_value DB_PROVIDER "$PROVIDER"
  set_env_value Database__Provider "$SETTINGS_PROVIDER"
  set_env_value ConnectionStrings__DefaultConnection "$CONNECTION"
  # Running this script through sudo (a Docker daemon that needs root, say) would otherwise
  # leave a root-owned file the developer's editor cannot open.
  if [[ -n "${SUDO_UID:-}" && -n "${SUDO_GID:-}" ]]; then
    chown "$SUDO_UID:$SUDO_GID" "$ENV_FILE" 2>/dev/null || true
  fi
  printf 'Wrote the %s connection to %s.\n' "$ENGINE" "${ENV_FILE#"$PROJECT_ROOT/"}"
}

# Sets one key in .env, replacing the existing assignment in place so the file keeps its
# order and comments, and appending it otherwise.
set_env_value() {
  local key="$1" value="$2"
  [[ -f "$ENV_FILE" ]] || : > "$ENV_FILE"
  NEXT_SAAS_ENV_FILE="$ENV_FILE" NEXT_SAAS_ENV_KEY="$key" NEXT_SAAS_ENV_VALUE="$value" node - <<'NODE'
const fs = require('fs')
const file = process.env.NEXT_SAAS_ENV_FILE
const key = process.env.NEXT_SAAS_ENV_KEY
const value = process.env.NEXT_SAAS_ENV_VALUE
const assignment = `${key}=${value}`
const lines = fs.readFileSync(file, 'utf8').split('\n')
const index = lines.findIndex(line => line.trimStart().startsWith(`${key}=`))
if (index >= 0) lines[index] = assignment
else {
  // Keep a single trailing newline whether or not the file already ended with one.
  while (lines.length && lines[lines.length - 1].trim() === '') lines.pop()
  lines.push(assignment)
}
fs.writeFileSync(file, `${lines.join('\n').replace(/\n+$/, '')}\n`)
NODE
}

container_state() {
  # Only a real container state counts as existing. A missing container, a template error, or
  # a runtime that reports the failure on stdout reads as absent, because anything else would
  # be taken for a container that can be started. A daemon this user cannot reach is reported
  # separately, since "absent" would send the caller off to create a container it cannot see.
  local status errors
  errors="$(mktemp)"
  status="$("$DOCKER" inspect --type container -f '{{.State.Status}}' "$CONTAINER" 2>"$errors")" || status=""
  if [[ -z "$status" ]] && grep -qiE 'permission denied|cannot connect to the docker daemon|is the docker daemon running' "$errors"; then
    status="unavailable"
  fi
  rm -f "$errors"
  case "$status" in
    created|running|paused|restarting|removing|exited|dead|unavailable) printf '%s' "$status" ;;
    *) printf 'absent' ;;
  esac
}

require_runtime() {
  [[ "$(container_state)" != "unavailable" ]] && return 0
  printf 'Cannot talk to %s. The daemon is not running, or this user cannot reach its socket.\n' "$DOCKER" >&2
  printf 'Add yourself to the docker group once, then log in again:\n' >&2
  printf '  sudo usermod -aG docker "$USER"\n' >&2
  printf 'Running this script with sudo also works, and it hands .env back to you afterwards.\n' >&2
  exit 2
}

ensure_network() {
  "$DOCKER" network inspect "$NETWORK" >/dev/null 2>&1 || "$DOCKER" network create "$NETWORK" >/dev/null
}

start_container() {
  local state
  require_runtime
  state="$(container_state)"
  if [[ "$state" == "running" ]]; then
    printf 'Local %s is already running as %s.\n' "$ENGINE" "$CONTAINER"
    return 0
  fi
  if [[ "$state" != "absent" ]]; then
    printf 'Starting existing container %s...\n' "$CONTAINER"
    if "$DOCKER" start "$CONTAINER" >/dev/null; then
      return 0
    fi
    # The record is stale or unstartable; drop it and create the container again rather than
    # leaving the developer with a database that never comes up.
    printf 'Could not start %s; recreating it.\n' "$CONTAINER"
    "$DOCKER" rm -f "$CONTAINER" >/dev/null 2>&1 || true
  fi

  ensure_network
  printf 'Creating %s from %s on port %s...\n' "$CONTAINER" "$IMAGE" "$PORT"
  case "$ENGINE" in
    postgres)
      # The same initializer the postgres accessory runs, so the local database and the
      # unprivileged next_saas login are created exactly as they are in production.
      "$DOCKER" run -d --name "$CONTAINER" --network "$NETWORK" \
        -p "$PORT:5432" \
        -e POSTGRES_USER=postgres \
        -e POSTGRES_DB=postgres \
        -e POSTGRES_PASSWORD="$DEV_PASSWORD" \
        -e DB_PASSWORD="$DEV_PASSWORD" \
        -v "$VOLUME:/var/lib/postgresql" \
        -v "$PROJECT_ROOT/config/db/postgres/init.sh:/docker-entrypoint-initdb.d/10-next-saas.sh:ro" \
        "$IMAGE" >/dev/null
      ;;
    mysql)
      # Like the accessory, the official image creates the database and application user.
      "$DOCKER" run -d --name "$CONTAINER" --network "$NETWORK" \
        -p "$PORT:3306" \
        -e MYSQL_DATABASE=next_saas \
        -e MYSQL_USER=next_saas \
        -e MYSQL_PASSWORD="$DEV_PASSWORD" \
        -e MYSQL_ROOT_PASSWORD="$DEV_PASSWORD" \
        -v "$VOLUME:/var/lib/mysql" \
        "$IMAGE" >/dev/null
      ;;
    sqlserver)
      "$DOCKER" run -d --name "$CONTAINER" --network "$NETWORK" \
        -p "$PORT:1433" \
        -e ACCEPT_EULA=Y \
        -e MSSQL_PID=Developer \
        -e MSSQL_SA_PASSWORD="$DEV_PASSWORD" \
        -v "$VOLUME:/var/opt/mssql" \
        "$IMAGE" >/dev/null
      ;;
  esac
}

wait_ready() {
  local attempt
  printf 'Waiting for %s to accept connections...\n' "$ENGINE"
  for attempt in $(seq 1 120); do
    case "$ENGINE" in
      postgres)
        # Query as the application login rather than probing the port: the entrypoint accepts
        # socket connections while the initializer is still creating the role and database.
        if "$DOCKER" exec -e PGPASSWORD="$DEV_PASSWORD" "$CONTAINER" \
            psql -U next_saas -d next_saas -c 'SELECT 1' >/dev/null 2>&1; then
          return 0
        fi
        ;;
      mysql)
        if "$DOCKER" exec -e MYSQL_PWD="$DEV_PASSWORD" "$CONTAINER" \
            mysql -u next_saas -D next_saas -e 'SELECT 1' >/dev/null 2>&1; then
          return 0
        fi
        ;;
      sqlserver)
        if sqlcmd_exec -Q 'SELECT 1' >/dev/null 2>&1; then
          return 0
        fi
        ;;
    esac
    if [[ "$(container_state)" == "exited" ]]; then
      "$DOCKER" logs --tail 40 "$CONTAINER" >&2
      printf '%s exited during startup.\n' "$CONTAINER" >&2
      exit 1
    fi
    sleep 2
  done
  "$DOCKER" logs --tail 40 "$CONTAINER" >&2
  printf '%s did not accept connections within 240 seconds.\n' "$ENGINE" >&2
  exit 1
}

# SQL Server's image ships no client, so drive it from the tools image on the same network,
# exactly as config/db/sqlserver/pre-deploy.sh does on the Kamal network.
sqlcmd_exec() {
  "$DOCKER" run --rm -i --network "$NETWORK" mcr.microsoft.com/mssql-tools18 \
    /opt/mssql-tools18/bin/sqlcmd -S "$CONTAINER" -U sa -P "$DEV_PASSWORD" -C -b "$@"
}

initialize_sqlserver() {
  # The same idempotent script the sqlserver destination runs before its first release.
  printf 'Ensuring the next_saas database and application login exist...\n'
  sqlcmd_exec -v app_password="$DEV_PASSWORD" -i /dev/stdin < "$PROJECT_ROOT/config/db/sqlserver/init.sql" >/dev/null
}

case "$COMMAND" in
  up)
    if [[ "$ENGINE" == "sqlite" ]]; then
      write_local_settings
      printf 'SQLite needs no server; MyApp/App_Data/app.db is created on first run.\n'
      exit 0
    fi
    start_container
    wait_ready
    if [[ "$ENGINE" == "sqlserver" ]]; then
      initialize_sqlserver
    fi
    write_local_settings
    printf 'Local %s is ready. Run migrations with: cd MyApp && npm run migrate\n' "$ENGINE"
    ;;
  down)
    if [[ "$ENGINE" == "sqlite" ]]; then
      printf 'SQLite runs no container. Use ./scripts/reset-dev.sh to discard local data.\n'
      exit 0
    fi
    "$DOCKER" rm -f "$CONTAINER" >/dev/null 2>&1 || true
    printf 'Removed %s. Its data volume %s was kept.\n' "$CONTAINER" "$VOLUME"
    ;;
  reset)
    if [[ "$ENGINE" == "sqlite" ]]; then
      printf 'Use: ASPNETCORE_ENVIRONMENT=Development ./scripts/reset-dev.sh --yes\n'
      exit 0
    fi
    "$DOCKER" rm -f "$CONTAINER" >/dev/null 2>&1 || true
    "$DOCKER" volume rm "$VOLUME" >/dev/null 2>&1 || true
    printf 'Removed %s and its data volume.\n' "$CONTAINER"
    start_container
    wait_ready
    if [[ "$ENGINE" == "sqlserver" ]]; then
      initialize_sqlserver
    fi
    write_local_settings
    printf 'Empty local %s database created.\n' "$ENGINE"
    ;;
  status)
    printf 'Provider:   %s\n' "$PROVIDER"
    if [[ "$ENGINE" != "$PROVIDER" ]]; then
      printf 'Engine:     %s (DEV_DB_ENGINE)\n' "$ENGINE"
    fi
    if [[ "$ENGINE" == "sqlite" ]]; then
      printf 'Database:   %s\n' "$PROJECT_ROOT/MyApp/App_Data/app.db"
    else
      printf 'Container:  %s (%s)\n' "$CONTAINER" "$(container_state)"
      printf 'Image:      %s\n' "$IMAGE"
      printf 'Connection: %s\n' "${CONNECTION/$DEV_PASSWORD/********}"
    fi
    if [[ -f "$ENV_FILE" ]] && grep -q '^Database__Provider=' "$ENV_FILE" 2>/dev/null; then
      printf 'Overrides:  %s (%s)\n' "${ENV_FILE#"$PROJECT_ROOT/"}" \
        "$(grep -m1 '^Database__Provider=' "$ENV_FILE" 2>/dev/null | cut -d= -f2- || printf 'unset')"
    else
      printf 'Dev JSON:   not written yet; run ./scripts/dev-db.sh up\n'
    fi
    ;;
  shell)
    case "$ENGINE" in
      sqlite) exec sqlite3 "$PROJECT_ROOT/MyApp/App_Data/app.db" ;;
      postgres) exec "$DOCKER" exec -it -e PGPASSWORD="$DEV_PASSWORD" "$CONTAINER" psql -U next_saas -d next_saas ;;
      mysql) exec "$DOCKER" exec -it -e MYSQL_PWD="$DEV_PASSWORD" "$CONTAINER" mysql -u next_saas -D next_saas ;;
      sqlserver) exec "$DOCKER" run --rm -it --network "$NETWORK" mcr.microsoft.com/mssql-tools18 \
        /opt/mssql-tools18/bin/sqlcmd -S "$CONTAINER" -U next_saas -P "$DEV_PASSWORD" -d next_saas -C ;;
    esac
    ;;
  env)
    # For tests, CI, or any shell that should target the same local database.
    printf 'export Database__Provider=%q\n' "$SETTINGS_PROVIDER"
    printf 'export ConnectionStrings__DefaultConnection=%q\n' "$CONNECTION"
    ;;
esac
