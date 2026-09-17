#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
VERIFY_TEMP="$(mktemp -d /tmp/next-saas-verify.XXXXXX)"
VERIFY_PID=""
cleanup() {
  if [[ -n "$VERIFY_PID" ]] && kill -0 "$VERIFY_PID" 2>/dev/null; then
    kill "$VERIFY_PID" 2>/dev/null || true
    wait "$VERIFY_PID" 2>/dev/null || true
  fi
  [[ "$VERIFY_TEMP" == /tmp/next-saas-verify.* ]] && rm -rf "$VERIFY_TEMP"
}
trap cleanup EXIT

cd "$PROJECT_ROOT"
dotnet build
dotnet test --no-build

cd "$PROJECT_ROOT/MyApp.Client"
npm run typecheck
npm run test:run
npm run build

cd "$PROJECT_ROOT/MyApp"
VERIFY_PORT="$((51000 + RANDOM % 1000))"
VERIFY_URL="http://127.0.0.1:$VERIFY_PORT"
# The empty-state gate is deliberately zero-dependency and asserts against the SQLite file
# below, so pin the provider too. An operator's own appsettings.Production.json is loaded by
# this Production run and may name a server provider, which this SQLite connection string
# would then contradict.
env "ASPNETCORE_ENVIRONMENT=Production" "Deployment__EnforceStartupChecks=false" "Database__Provider=Sqlite" "ConnectionStrings__DefaultConnection=Data Source=$VERIFY_TEMP/empty.db;Cache=Shared" "FileStorage__RootPath=$VERIFY_TEMP/files" \
  dotnet run --no-build --no-launch-profile --urls "$VERIFY_URL" >"$VERIFY_TEMP/app.log" 2>&1 &
VERIFY_PID="$!"
READY="false"
for _ in $(seq 1 120); do
  if curl --fail --silent "$VERIFY_URL/ready" >/dev/null 2>&1; then
    READY="true"
    break
  fi
  if ! kill -0 "$VERIFY_PID" 2>/dev/null; then break; fi
  sleep 0.25
done
if [[ "$READY" != "true" ]]; then
  cat "$VERIFY_TEMP/app.log" >&2
  echo "Application did not become ready after empty-state bootstrap." >&2
  exit 1
fi
curl --silent --show-error -D "$VERIFY_TEMP/security-headers.txt" -o /dev/null "$VERIFY_URL/"
if ! grep -qi '^Content-Security-Policy:' "$VERIFY_TEMP/security-headers.txt" ||
   ! grep -qi '^X-Content-Type-Options: nosniff' "$VERIFY_TEMP/security-headers.txt"; then
  cat "$VERIFY_TEMP/security-headers.txt" >&2
  echo "Production security headers were not applied." >&2
  exit 1
fi
CSRF_STATUS="$(curl --silent --output "$VERIFY_TEMP/csrf.json" --write-out '%{http_code}' \
  -X POST -H 'Cookie: .AspNetCore.Identity.Application=test' -H 'Content-Type: application/json' \
  -d '{}' "$VERIFY_URL/saas/workspace")"
if [[ "$CSRF_STATUS" != "403" ]] || ! grep -q 'CsrfValidationFailed' "$VERIFY_TEMP/csrf.json"; then
  echo "Cookie-authenticated SaaS mutations are not protected by same-origin validation." >&2
  exit 1
fi
AUTH_STATUS=""
for _ in $(seq 1 21); do
  AUTH_STATUS="$(curl --silent --output "$VERIFY_TEMP/auth-rate-limit.json" --write-out '%{http_code}' \
    -X POST -H 'Content-Type: application/json' -d '{}' "$VERIFY_URL/api/Authenticate")"
done
if [[ "$AUTH_STATUS" != "429" ]] || ! grep -q 'AuthenticationRateLimitExceeded' "$VERIFY_TEMP/auth-rate-limit.json"; then
  echo "Sensitive authentication routes are not protected by rate limiting." >&2
  exit 1
fi
kill "$VERIFY_PID"
wait "$VERIFY_PID" 2>/dev/null || true
VERIFY_PID=""
PLAN_COUNT="$(sqlite3 "$VERIFY_TEMP/empty.db" "select count(*) from SaasPlan;")"
if [[ "$PLAN_COUNT" != "4" ]]; then
  echo "Empty-state migration did not seed the four reference plans." >&2
  exit 1
fi
PREFERENCE_TABLE="$(sqlite3 "$VERIFY_TEMP/empty.db" "select count(*) from sqlite_master where type='table' and name='UserWorkspacePreference';")"
if [[ "$PREFERENCE_TABLE" != "1" ]]; then
  echo "Empty-state bootstrap did not create the active-workspace preference table." >&2
  exit 1
fi
RETENTION_TABLES="$(sqlite3 "$VERIFY_TEMP/empty.db" "select count(*) from sqlite_master where type='table' and name in ('WorkspaceRetentionPolicy','DataRetentionRun');")"
if [[ "$RETENTION_TABLES" != "2" ]]; then
  echo "Empty-state bootstrap did not create the data-retention tables." >&2
  exit 1
fi

cd "$PROJECT_ROOT"
echo "All next-saas verification gates passed."
