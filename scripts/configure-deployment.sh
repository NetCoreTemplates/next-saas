#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CONFIG_PATH="$PROJECT_ROOT/MyApp/appsettings.Production.json"
# config/deploy.yml loads .env through ERB, so honour the same file here.
# shellcheck disable=SC1091
. "$SCRIPT_DIR/load-env.sh"
load_env_file "$PROJECT_ROOT/.env"
PROVIDER="${DB_PROVIDER:-sqlite}"
SERVICE_NAME=""
REPOSITORY=""
SET_GITHUB_SECRETS="false"
BACKUP_PATH=""
TEMP_PATH=""

usage() {
  cat <<'EOF'
Usage: ./scripts/configure-deployment.sh --provider <provider> --service <service> [options]

Points the ignored production JSON at the chosen database provider, validates it, and
optionally uploads the matching GitHub Actions secrets and DB_PROVIDER variable.

Options:
  --provider <name>         sqlite, postgres, mysql, or sqlserver
                            (default: $DB_PROVIDER, else sqlite).
  --service <service>       Kamal service name, used to resolve the database host.
  --json <path>             Production JSON to update.
  --repo <owner/name>       GitHub repository for secret updates.
  --set-github-secrets      Upload APPSETTINGS_JSON, DB_PROVIDER, and provider credentials.
  -h, --help                Show this help.

For a provider that runs a database server, DB_PASSWORD is read from the environment or
.env, or prompted for without echo. The existing JSON is backed up before it is changed.
EOF
}

cleanup() {
  [[ -n "$TEMP_PATH" && -f "$TEMP_PATH" ]] && rm -f "$TEMP_PATH"
}
trap cleanup EXIT

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --provider)
      shift
      [[ "$#" -gt 0 ]] || { printf 'Missing value after --provider\n' >&2; exit 2; }
      PROVIDER="$1"
      ;;
    --service)
      shift
      [[ "$#" -gt 0 ]] || { printf 'Missing value after --service\n' >&2; exit 2; }
      SERVICE_NAME="$1"
      ;;
    --json)
      shift
      [[ "$#" -gt 0 ]] || { printf 'Missing value after --json\n' >&2; exit 2; }
      CONFIG_PATH="$1"
      ;;
    --repo)
      shift
      [[ "$#" -gt 0 ]] || { printf 'Missing value after --repo\n' >&2; exit 2; }
      REPOSITORY="$1"
      ;;
    --set-github-secrets) SET_GITHUB_SECRETS="true" ;;
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
if [[ ! "$SERVICE_NAME" =~ ^[a-z0-9][a-z0-9-]*$ ]]; then
  printf 'Refusing configuration: --service must contain only lowercase letters, numbers, and hyphens.\n' >&2
  exit 2
fi
for tool in node; do
  command -v "$tool" >/dev/null 2>&1 || { printf '%s is required.\n' "$tool" >&2; exit 2; }
done
if [[ ! -f "$CONFIG_PATH" ]]; then
  printf 'Production configuration not found: %s\n' "$CONFIG_PATH" >&2
  printf 'Copy config/appsettings.deploy.%s.example.json there first, then customize its non-database settings.\n' "$PROVIDER" >&2
  exit 2
fi

if [[ "$PROVIDER" != "sqlite" ]]; then
  if [[ -z "${DB_PASSWORD:-}" ]]; then
    read -r -s -p 'Database password: ' DB_PASSWORD
    printf '\n'
  fi
  # This value is generated, never typed, so require real entropy. It is also embedded in
  # an ADO.NET connection string, where a semicolon would terminate the Password field.
  if (( ${#DB_PASSWORD} < 24 )); then
    printf 'DB_PASSWORD must be at least 24 characters. Generate one with: openssl rand -hex 32\n' >&2
    exit 2
  fi
  if [[ "$DB_PASSWORD" == *';'* || "$DB_PASSWORD" == *$'\n'* || "$DB_PASSWORD" == *$'\r'* ]]; then
    printf 'DB_PASSWORD cannot contain a semicolon, carriage return, or newline.\n' >&2
    exit 2
  fi
  if [[ "$PROVIDER" == "sqlserver" ]]; then
    # SQL Server refuses to start when sa fails its password policy: at least eight
    # characters drawn from three of uppercase, lowercase, digits, and symbols. A hex
    # password satisfies only two, so reject it here rather than at container boot.
    categories=0
    [[ "$DB_PASSWORD" =~ [A-Z] ]] && categories=$((categories + 1))
    [[ "$DB_PASSWORD" =~ [a-z] ]] && categories=$((categories + 1))
    [[ "$DB_PASSWORD" =~ [0-9] ]] && categories=$((categories + 1))
    [[ "$DB_PASSWORD" =~ [^A-Za-z0-9] ]] && categories=$((categories + 1))
    if (( categories < 3 )); then
      printf 'SQL Server requires DB_PASSWORD to use three of uppercase, lowercase, digits, and symbols.\n' >&2
      printf 'A hex password satisfies only two. Generate one with:\n' >&2
      printf "  openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c 40\n" >&2
      exit 2
    fi
  fi
fi

CONFIG_DIR="$(cd "$(dirname "$CONFIG_PATH")" && pwd)"
CONFIG_PATH="$CONFIG_DIR/$(basename "$CONFIG_PATH")"
BACKUP_PATH="$CONFIG_PATH.backup.$(date -u +%Y%m%dT%H%M%SZ)"
TEMP_PATH="$(mktemp "$CONFIG_PATH.tmp.XXXXXX")"
cp "$CONFIG_PATH" "$BACKUP_PATH"
chmod 0600 "$BACKUP_PATH"

export NEXT_SAAS_CONFIG_PATH="$CONFIG_PATH"
export NEXT_SAAS_CONFIG_TEMP="$TEMP_PATH"
export NEXT_SAAS_SERVICE="$SERVICE_NAME"
export NEXT_SAAS_PROVIDER="$PROVIDER"
export DB_PASSWORD="${DB_PASSWORD:-}"
node - <<'NODE'
const fs = require('fs')
const source = process.env.NEXT_SAAS_CONFIG_PATH
const target = process.env.NEXT_SAAS_CONFIG_TEMP
const service = process.env.NEXT_SAAS_SERVICE
const provider = process.env.NEXT_SAAS_PROVIDER
const password = process.env.DB_PASSWORD
const config = JSON.parse(fs.readFileSync(source, 'utf8'))

// Each provider owns the Database, ConnectionStrings, and Deployment policy it implies.
// Every other setting in the file is preserved as-is.
const providers = {
  sqlite: () => {
    config.Database = { ...(config.Database ?? {}), Provider: 'Sqlite', AutoMigrateEmpty: true }
    config.ConnectionStrings = {
      ...(config.ConnectionStrings ?? {}),
      DefaultConnection: 'Data Source=App_Data/app.db;Cache=Shared',
    }
    config.Deployment = {
      ...(config.Deployment ?? {}),
      RequireNetworkDatabase: false,
      RequireExplicitMigrations: false,
    }
  },
  sqlserver: () => {
    config.Database = { ...(config.Database ?? {}), Provider: 'SqlServer', AutoMigrateEmpty: false }
    config.ConnectionStrings = {
      ...(config.ConnectionStrings ?? {}),
      // The accessory serves a self-signed certificate on the private Kamal network.
      DefaultConnection: `Server=${service}-sqlserver,1433;Database=next_saas;User Id=next_saas;Password=${password};Encrypt=True;TrustServerCertificate=True`,
    }
    config.Deployment = {
      ...(config.Deployment ?? {}),
      RequireNetworkDatabase: true,
      RequireExplicitMigrations: true,
    }
  },
  mysql: () => {
    config.Database = { ...(config.Database ?? {}), Provider: 'MySql', AutoMigrateEmpty: false }
    config.ConnectionStrings = {
      ...(config.ConnectionStrings ?? {}),
      DefaultConnection: `Server=${service}-mysql;Port=3306;Database=next_saas;User Id=next_saas;Password=${password};SslMode=Preferred`,
    }
    config.Deployment = {
      ...(config.Deployment ?? {}),
      RequireNetworkDatabase: true,
      RequireExplicitMigrations: true,
    }
  },
  postgres: () => {
    config.Database = { ...(config.Database ?? {}), Provider: 'PostgreSql', AutoMigrateEmpty: false }
    config.ConnectionStrings = {
      ...(config.ConnectionStrings ?? {}),
      DefaultConnection: `Host=${service}-postgres;Port=5432;Database=next_saas;Username=next_saas;Password=${password};SSL Mode=Disable`,
    }
    config.Deployment = {
      ...(config.Deployment ?? {}),
      RequireNetworkDatabase: true,
      RequireExplicitMigrations: true,
    }
  },
}
providers[provider]()
delete config.Deployment.RequirePostgreSql
fs.writeFileSync(target, `${JSON.stringify(config, null, 2)}\n`, { mode: 0o600 })
NODE

mv "$TEMP_PATH" "$CONFIG_PATH"
TEMP_PATH=""
chmod 0600 "$CONFIG_PATH"

printf 'Updated %s for the %s provider on service %s.\n' "$CONFIG_PATH" "$PROVIDER" "$SERVICE_NAME"
printf 'Backup: %s\n' "$BACKUP_PATH"

"$SCRIPT_DIR/preflight.sh" --json "$CONFIG_PATH" --config-only

if [[ "$SET_GITHUB_SECRETS" == "true" ]]; then
  command -v gh >/dev/null 2>&1 || { printf 'gh is required for --set-github-secrets.\n' >&2; exit 2; }
  if [[ -z "$REPOSITORY" ]]; then
    printf -- '--repo <owner/name> is required with --set-github-secrets.\n' >&2
    exit 2
  fi
  if [[ "$PROVIDER" != "sqlite" ]]; then
    printf '%s' "$DB_PASSWORD" | gh secret set DB_PASSWORD --repo "$REPOSITORY"
  fi
  gh secret set APPSETTINGS_JSON --repo "$REPOSITORY" < "$CONFIG_PATH"
  gh variable set DB_PROVIDER --repo "$REPOSITORY" --body "$PROVIDER"
  printf 'Updated APPSETTINGS_JSON, DB_PROVIDER=%s, and %s credentials in %s.\n' "$PROVIDER" "$PROVIDER" "$REPOSITORY"
else
  printf 'GitHub secrets were not changed. Re-run with --repo <owner/name> --set-github-secrets when ready.\n'
  printf 'Release also needs the DB_PROVIDER repository variable set to %s.\n' "$PROVIDER"
fi
