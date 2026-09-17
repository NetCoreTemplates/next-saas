#!/usr/bin/env bash
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ERRORS=0
WARNINGS=0
CONFIG_ONLY=""
JSON_CONFIG=""

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --config-only) CONFIG_ONLY="--config-only" ;;
    --json)
      shift
      [[ "$#" -gt 0 ]] || { printf 'Missing file after --json\n' >&2; exit 2; }
      JSON_CONFIG="$1"
      ;;
    *) printf 'Unknown argument: %s\n' "$1" >&2; exit 2 ;;
  esac
  shift
done

if [[ -n "$JSON_CONFIG" ]]; then
  [[ -f "$JSON_CONFIG" ]] || { printf 'JSON configuration not found: %s\n' "$JSON_CONFIG" >&2; exit 2; }
  node -e 'JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"))' "$JSON_CONFIG" || exit 2
  while IFS= read -r -d '' entry; do
    name="${entry%%=*}"
    value="${entry#*=}"
    printf -v "$name" '%s' "$value"
    export "$name"
  done < <(node "$SCRIPT_DIR/json-config-env.mjs" "$JSON_CONFIG")
fi

pass() { printf '  [ok] %s\n' "$1"; }
warn() { printf '  [warn] %s\n' "$1"; WARNINGS=$((WARNINGS + 1)); }
fail() { printf '  [error] %s\n' "$1"; ERRORS=$((ERRORS + 1)); }
required() { local name="$1" label="$2"; [[ -n "${!name:-}" ]] && pass "$label is supplied" || fail "$label is required"; }
enabled() { [[ "${1,,}" == "true" ]]; }

printf 'next-saas production preflight\n'
[[ -n "$JSON_CONFIG" ]] && printf '  configuration: %s\n' "$JSON_CONFIG"
printf '\n'

BASE_URL="${AppConfig__BaseUrl:-}"
if [[ "$BASE_URL" =~ ^https://[^/]+ ]]; then pass "public HTTPS base URL is configured"; else fail "AppConfig__BaseUrl must be an HTTPS public URL"; fi
if [[ "$BASE_URL" == *localhost* || "$BASE_URL" == *example.com* ]]; then fail "AppConfig__BaseUrl still uses a placeholder host"; fi

ALLOWED="${AllowedHosts:-}"
if [[ -n "$ALLOWED" && "$ALLOWED" != "*" ]]; then pass "AllowedHosts is restricted"; else fail "AllowedHosts must be set to the deployed hostname"; fi

if enabled "${Deployment__RequireNetworkDatabase:-true}"; then
  if [[ "${Database__Provider:-}" == "Sqlite" || -z "${Database__Provider:-}" ]]; then
    fail "Database__Provider must be a networked database server for the default production policy"
  else
    pass "networked database provider ${Database__Provider} is selected"
  fi
  required ConnectionStrings__DefaultConnection "database connection string"
else
  required ConnectionStrings__DefaultConnection "database connection string"
  if [[ "${Database__Provider:-}" == "Sqlite" ]]; then
    warn "SQLite is explicitly allowed; use one application instance and preserve App_Data"
  else
    pass "the relaxed database policy is explicitly allowed"
  fi
fi

if enabled "${Deployment__RequireSmtp:-true}"; then
  if [[ "${Notifications__Provider:-}" == "Smtp" ]]; then pass "SMTP delivery is selected"; else fail "Notifications__Provider must be Smtp"; fi
  required SmtpConfig__Host "SMTP host"
  required SmtpConfig__FromEmail "SMTP sender"
else
  warn "SMTP is not required by this deployment; invitations and account recovery may not be deliverable"
fi
SUPPORT="${Product__SupportEmail:-}"
if [[ "$SUPPORT" == *@* && "$SUPPORT" != *@example.com ]]; then pass "support email is customized"; else fail "Product__SupportEmail must be a monitored non-placeholder address"; fi

BOOTSTRAP_EMAIL="${BootstrapAdmin__Email:-}"
BOOTSTRAP_PASSWORD="${BootstrapAdmin__Password:-}"
if [[ -n "$BOOTSTRAP_EMAIL" || -n "$BOOTSTRAP_PASSWORD" ]]; then
  if [[ "$BOOTSTRAP_EMAIL" == *@* && "$BOOTSTRAP_EMAIL" != *@example.com ]]; then pass "bootstrap administrator email is customized"; else fail "BootstrapAdmin__Email must be a non-placeholder email"; fi
  if [[ ${#BOOTSTRAP_PASSWORD} -ge 12 && "$BOOTSTRAP_PASSWORD" != REPLACE* && "$BOOTSTRAP_PASSWORD" =~ [A-Z] && "$BOOTSTRAP_PASSWORD" =~ [a-z] && "$BOOTSTRAP_PASSWORD" =~ [0-9] && "$BOOTSTRAP_PASSWORD" =~ [^[:alnum:]] ]]; then
    pass "bootstrap administrator password satisfies the Identity policy"
  else
    fail "BootstrapAdmin__Password must be unique, at least 12 characters, and include upper/lowercase, a number, and a symbol"
  fi
else
  warn "no bootstrap administrator is configured; confirm an Admin user already exists"
fi

if enabled "${Deployment__RequireStripe:-true}"; then
  STRIPE_SECRET="${Stripe__SecretKey:-${STRIPE_SECRET_KEY:-}}"
  STRIPE_PUBLIC="${Stripe__PublishableKey:-}"
  STRIPE_WEBHOOK="${Stripe__WebhookSecret:-${STRIPE_WEBHOOK_SECRET:-}}"
  if [[ "$STRIPE_SECRET" == sk_live_* || "$STRIPE_SECRET" == rk_live_* ]]; then
    pass "Stripe live secret is configured"
  elif [[ ( "$STRIPE_SECRET" == sk_test_* || "$STRIPE_SECRET" == rk_test_* ) && "${Deployment__AllowTestStripeKeys:-false}" == "true" ]]; then
    warn "Stripe test mode is explicitly enabled for this Production environment"
  else
    fail "Stripe live secret is required (or explicitly allow test keys for staging)"
  fi
  [[ "$STRIPE_PUBLIC" == pk_live_* || ( "$STRIPE_PUBLIC" == pk_test_* && "${Deployment__AllowTestStripeKeys:-false}" == "true" ) ]] && pass "Stripe publishable key matches deployment mode" || fail "Stripe publishable key is missing or has the wrong mode"
  if enabled "${Deployment__RequireStripeWebhook:-true}"; then
    [[ "$STRIPE_WEBHOOK" == whsec_* ]] && pass "Stripe webhook signing secret is configured" || fail "Stripe__WebhookSecret must be configured"
  elif [[ "$STRIPE_WEBHOOK" == whsec_* ]]; then
    pass "optional Stripe webhook signing secret is configured"
  else
    warn "Stripe webhook verification is not required by this deployment"
  fi
else
  warn "Stripe is not required by this deployment; paid checkout is unavailable"
fi

if [[ "${ASPNETCORE_FORWARDEDHEADERS_ENABLED:-}" == "true" ]]; then pass "forwarded headers are enabled"; else warn "enable ASPNETCORE_FORWARDEDHEADERS_ENABLED behind a reverse proxy"; fi

if [[ "$ERRORS" -eq 0 && "$CONFIG_ONLY" != "--config-only" ]]; then
  printf '\nRunning build, tests, static export, and empty-state bootstrap...\n'
  if "$SCRIPT_DIR/verify.sh"; then pass "verification suite passed"; else fail "verification suite failed"; fi
fi

printf '\nPreflight finished with %d error(s) and %d warning(s).\n' "$ERRORS" "$WARNINGS"
[[ "$ERRORS" -eq 0 ]]
