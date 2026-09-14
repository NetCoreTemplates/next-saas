#!/usr/bin/env bash
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ERRORS=0
WARNINGS=0

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
DATABASE_PROVIDER="${Database__Provider:-$(node -p "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).Database?.Provider ?? 'Sqlite'" "$APP_SETTINGS" 2>/dev/null)}"

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

if [[ "$DATABASE_PROVIDER" == "Sqlite" ]]; then
  if ! command -v sqlite3 >/dev/null 2>&1; then
    fail "sqlite3 is required when Database.Provider is Sqlite"
  else
    pass "database provider is SQLite"
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
elif [[ "$DATABASE_PROVIDER" == "PostgreSql" || "$DATABASE_PROVIDER" == "Postgres" ]]; then
  pass "database provider is PostgreSQL"
  if [[ -z "${ConnectionStrings__DefaultConnection:-}" ]]; then
    warn "set ConnectionStrings__DefaultConnection in the environment for PostgreSQL"
  elif command -v pg_isready >/dev/null 2>&1; then
    pass "PostgreSQL client tools are installed"
  else
    warn "pg_isready is not installed; connectivity was not checked"
  fi
else
  fail "Database.Provider must be Sqlite or PostgreSql"
fi

printf '\nDoctor finished with %d error(s) and %d warning(s).\n' "$ERRORS" "$WARNINGS"
[[ "$ERRORS" -eq 0 ]]
