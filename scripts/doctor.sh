#!/usr/bin/env bash
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ERRORS=0
WARNINGS=0

# config/deploy.yml loads .env through ERB, so honour the same file and precedence.
# shellcheck disable=SC1091
. "$SCRIPT_DIR/load-env.sh"
load_env_file "$PROJECT_ROOT/.env"

pass() { printf '  [ok] %s\n' "$1"; }
warn() { printf '  [warn] %s\n' "$1"; WARNINGS=$((WARNINGS + 1)); }
fail() { printf '  [error] %s\n' "$1"; ERRORS=$((ERRORS + 1)); }

printf 'next-saas doctor\n\n'

for tool in dotnet node npm; do
  if command -v "$tool" >/dev/null 2>&1; then
    pass "$tool is installed"
  else
    fail "$tool is required but was not found"
  fi
done

APP_SETTINGS="$PROJECT_ROOT/MyApp/appsettings.json"
PLAN_SETTINGS="$PROJECT_ROOT/MyApp/plans.json"

if node -e "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8'))" "$APP_SETTINGS" 2>/dev/null; then
  pass "appsettings.json is valid JSON"
else
  fail "MyApp/appsettings.json is not valid JSON"
fi

if node -e "const plans=JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')); if(!Array.isArray(plans)||plans.length===0)process.exit(1); const codes=plans.map(x=>x.Code); if(new Set(codes).size!==codes.length||codes.some(x=>!x))process.exit(1)" "$PLAN_SETTINGS" 2>/dev/null; then
  pass "plans.json contains a non-empty catalog with unique plan codes"
else
  fail "MyApp/plans.json must contain plans with unique, non-empty Code values"
fi

PRODUCT_NAME="$(node -p "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).Product?.ProductName ?? ''" "$APP_SETTINGS" 2>/dev/null)"
SUPPORT_EMAIL="$(node -p "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).Product?.SupportEmail ?? ''" "$APP_SETTINGS" 2>/dev/null)"
EMAIL_PROVIDER="$(node -p "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).Notifications?.Provider ?? 'Development'" "$APP_SETTINGS" 2>/dev/null)"
LOCAL_SETTINGS="$PROJECT_ROOT/MyApp/appsettings.Development.json"
# The effective local provider: Database__Provider from the environment or the private .env
# that scripts/dev-db.sh writes, then the source-controlled Development default.
DATABASE_PROVIDER="${Database__Provider:-}"
if [[ -z "$DATABASE_PROVIDER" && -f "$LOCAL_SETTINGS" ]]; then
  DATABASE_PROVIDER="$(node -p "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).Database?.Provider ?? ''" "$LOCAL_SETTINGS" 2>/dev/null)"
fi
if [[ -z "$DATABASE_PROVIDER" ]]; then
  DATABASE_PROVIDER="$(node -p "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).Database?.Provider ?? 'Sqlite'" "$APP_SETTINGS" 2>/dev/null)"
fi
normalize_provider() {
  case "$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')" in
    sqlite) printf 'sqlite' ;;
    postgres|postgresql) printf 'postgres' ;;
    sqlserver|mssql) printf 'sqlserver' ;;
    mysql|mariadb) printf 'mysql' ;;
    *) printf 'unsupported' ;;
  esac
}
LOCAL_PROVIDER="$(normalize_provider "$DATABASE_PROVIDER")"

if [[ -n "$PRODUCT_NAME" ]]; then pass "product name is configured as $PRODUCT_NAME"; else fail "Product.ProductName is required"; fi
if [[ "$SUPPORT_EMAIL" == *"@"* ]]; then pass "support email is configured"; else fail "Product.SupportEmail must be an email address"; fi

case "$EMAIL_PROVIDER" in
  Development) pass "email provider is Development; confirmation links are shown locally" ;;
  Disabled) warn "email is disabled; account recovery and invitations cannot be delivered" ;;
  Smtp)
    if node -e "const x=JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).SmtpConfig; if(!x?.Host||!x?.FromEmail)process.exit(1)" "$APP_SETTINGS" 2>/dev/null; then
      pass "SMTP provider has a host and sender address"
    else
      fail "Notifications.Provider is Smtp but SmtpConfig.Host or FromEmail is missing"
    fi
    ;;
  *) fail "Notifications.Provider must be Development, Smtp, or Disabled" ;;
esac

STRIPE_SECRET="${STRIPE_SECRET_KEY:-$(node -p "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).Stripe?.SecretKey ?? ''" "$APP_SETTINGS" 2>/dev/null)}"
STRIPE_WEBHOOK="${STRIPE_WEBHOOK_SECRET:-$(node -p "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).Stripe?.WebhookSecret ?? ''" "$APP_SETTINGS" 2>/dev/null)}"
if [[ -z "$STRIPE_SECRET" ]]; then
  warn "Stripe is not configured; free plans work but checkout is unavailable"
elif [[ "$STRIPE_SECRET" == sk_test_* || "$STRIPE_SECRET" == rk_test_* ]]; then
  pass "Stripe sandbox key is configured"
elif [[ "$STRIPE_SECRET" == sk_live_* || "$STRIPE_SECRET" == rk_live_* ]]; then
  warn "a live Stripe key is configured; verify deployment intent before provisioning catalog data"
else
  fail "Stripe secret key has an unexpected format"
fi
if [[ -n "$STRIPE_SECRET" && -z "$STRIPE_WEBHOOK" ]]; then warn "Stripe checkout is configured but webhook verification is not"; fi

case "$LOCAL_PROVIDER" in
  sqlite)
    if ! command -v sqlite3 >/dev/null 2>&1; then
      fail "sqlite3 is required when Database.Provider is Sqlite"
    else
      pass "local database provider is SQLite"
      DATABASE="$PROJECT_ROOT/MyApp/App_Data/app.db"
      if [[ -f "$DATABASE" ]]; then
        if [[ "$(sqlite3 "$DATABASE" "select count(*) from sqlite_master where type='table' and name='SaasPlan';" 2>/dev/null)" == "1" ]]; then
          pass "development database contains the SaaS schema"
        else
          fail "development database exists but does not contain the SaaS schema"
        fi
      else
        pass "no development database exists; it will be created on first migration or startup"
      fi
    fi
    ;;
  postgres|mysql|sqlserver)
    pass "local database provider is $LOCAL_PROVIDER"
    CONTAINER="next-saas-dev-$LOCAL_PROVIDER"
    RUNTIME=""
    for candidate in docker podman; do
      command -v "$candidate" >/dev/null 2>&1 && { RUNTIME="$candidate"; break; }
    done
    if [[ -z "$RUNTIME" ]]; then
      warn "docker or podman is not installed; ./scripts/dev-db.sh cannot run $LOCAL_PROVIDER locally"
    else
      # Only a real container state counts as existing; anything else reads as absent.
      CONTAINER_ERRORS="$(mktemp)"
      CONTAINER_STATE="$("$RUNTIME" inspect --type container -f '{{.State.Status}}' "$CONTAINER" 2>"$CONTAINER_ERRORS")" || CONTAINER_STATE=""
      if [[ -z "$CONTAINER_STATE" ]] && grep -qiE 'permission denied|cannot connect to the docker daemon|is the docker daemon running' "$CONTAINER_ERRORS"; then
        CONTAINER_STATE="unavailable"
      fi
      rm -f "$CONTAINER_ERRORS"
      case "$CONTAINER_STATE" in
        running) pass "local $LOCAL_PROVIDER container $CONTAINER is running" ;;
        created|paused|restarting|removing|exited|dead)
          warn "local $LOCAL_PROVIDER container $CONTAINER is $CONTAINER_STATE; start it with ./scripts/dev-db.sh up" ;;
        unavailable) warn "cannot query $RUNTIME; the daemon is not running or this user cannot reach its socket" ;;
        *) warn "no local $LOCAL_PROVIDER database is running; start it with ./scripts/dev-db.sh up" ;;
      esac
    fi
    ;;
  *)
    fail "Database.Provider must be Sqlite, PostgreSql, MySql, or SqlServer"
    ;;
esac

# Local and deployed providers are meant to be the same engine.
DEPLOY_PROVIDER="$(normalize_provider "${DB_PROVIDER:-sqlite}")"
if [[ "$DEPLOY_PROVIDER" == "unsupported" ]]; then
  fail "DB_PROVIDER must be sqlite, postgres, mysql, or sqlserver"
elif [[ "$LOCAL_PROVIDER" != "unsupported" && "$LOCAL_PROVIDER" != "$DEPLOY_PROVIDER" ]]; then
  warn "local database is $LOCAL_PROVIDER but DB_PROVIDER deploys $DEPLOY_PROVIDER; run ./scripts/dev-db.sh up"
else
  pass "local and deployed database providers match ($DEPLOY_PROVIDER)"
fi

printf '\nDoctor finished with %d error(s) and %d warning(s).\n' "$ERRORS" "$WARNINGS"
[[ "$ERRORS" -eq 0 ]]
