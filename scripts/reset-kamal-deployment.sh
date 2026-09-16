#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# config/deploy.yml loads .env through ERB, so honour the same file here.
# shellcheck disable=SC1091
. "$SCRIPT_DIR/load-env.sh"
load_env_file "$PROJECT_ROOT/.env"
SERVICE_NAME=""
PROVIDER="${DB_PROVIDER:-sqlite}"
CONFIRMED="false"

usage() {
  cat <<'EOF'
Usage: ./scripts/reset-kamal-deployment.sh --service <service> [--provider <name>] [--yes]

Removes only the named Kamal application's containers, images, proxy registration,
per-destination app directories, database accessories, and its state under
/opt/docker/<service>. The shared Kamal proxy, Docker network, other services,
GitHub secrets, and external services are preserved.

  --provider <name>  Kamal destination used to load configuration and secrets,
                     so it must be a provider this repository defines (default
                     \$DB_PROVIDER, else sqlite). It does NOT narrow what is removed: every accessory
                     and app directory belonging to the service is removed
                     regardless, because the point is a clean slate for the next
                     deployment whichever provider that uses.

Without --yes the script prints the resolved targets and exits without changes.
EOF
}

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --service)
      shift
      [[ "$#" -gt 0 ]] || { printf 'Missing value after --service\n' >&2; exit 2; }
      SERVICE_NAME="$1"
      ;;
    --provider)
      shift
      [[ "$#" -gt 0 ]] || { printf 'Missing value after --provider\n' >&2; exit 2; }
      PROVIDER="$1"
      ;;
    --yes) CONFIRMED="true" ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'Unknown argument: %s\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done

if [[ ! "$SERVICE_NAME" =~ ^[a-z0-9][a-z0-9-]*$ ]]; then
  printf 'Refusing reset: --service must contain only lowercase letters, numbers, and hyphens.\n' >&2
  exit 2
fi
if [[ ! -f "$PROJECT_ROOT/config/deploy.$PROVIDER.yml" ]]; then
  printf 'Refusing reset: unknown --provider %s. Available providers:\n' "$PROVIDER" >&2
  ls "$PROJECT_ROOT"/config/deploy.*.yml | sed 's|.*/config/deploy\.\(.*\)\.yml|  \1|' >&2
  exit 2
fi

REMOTE_ROOT="/opt/docker/$SERVICE_NAME"

# A reset must leave nothing behind for ANY provider, not just the one passed in:
# resetting a PostgreSQL deployment in order to move to SQLite must still remove the
# PostgreSQL container, or its data directory would be deleted while it still runs.
# Accessory containers are named "<service>-<accessory>" across every destination file.
DESTINATIONS=()
ACCESSORY_CONTAINERS=()
for overlay in "$PROJECT_ROOT"/config/deploy.*.yml; do
  destination="$(basename "$overlay" .yml)"
  DESTINATIONS+=("${destination#deploy.}")
  while IFS= read -r accessory; do
    ACCESSORY_CONTAINERS+=("$SERVICE_NAME-$accessory")
  done < <(awk '
    /^accessories:/ { in_accessories = 1; next }
    /^[^[:space:]#]/ { in_accessories = 0 }
    in_accessories && /^  [A-Za-z0-9_-]+:[[:space:]]*$/ { gsub(/[ :]/, ""); print }
  ' "$overlay")
done
# De-duplicate accessories declared by more than one destination.
if (( ${#ACCESSORY_CONTAINERS[@]} )); then
  mapfile -t ACCESSORY_CONTAINERS < <(printf '%s\n' "${ACCESSORY_CONTAINERS[@]}" | sort -u)
fi

# kamal-proxy registers one service per role per scope, named "<service>-<role>[-<destination>]".
ROLES=()
while IFS= read -r role; do ROLES+=("$role"); done < <(awk '
  /^servers:/ { in_servers = 1; next }
  /^[^[:space:]#]/ { in_servers = 0 }
  in_servers && /^  [A-Za-z0-9_-]+:[[:space:]]*$/ { gsub(/[ :]/, ""); print }
' "$PROJECT_ROOT/config/deploy.yml")
PROXY_SERVICES=()
for role in "${ROLES[@]}"; do
  PROXY_SERVICES+=("$SERVICE_NAME-$role")
  for destination in "${DESTINATIONS[@]}"; do
    PROXY_SERVICES+=("$SERVICE_NAME-$role-$destination")
  done
done

# Kamal stores env files (including APPSETTINGS_JSON_BASE64) in ~/.kamal/apps/<service>-<destination>,
# and `kamal app remove` only removes the one for the destination it was given. The bare
# "<service>" entry covers deployments made before this repository used destinations.
# These are exact paths, never globs, so a similarly named service is never touched.
APP_DIRECTORIES=("$SERVICE_NAME")
for destination in "${DESTINATIONS[@]}"; do
  APP_DIRECTORIES+=("$SERVICE_NAME-$destination")
done

printf 'Kamal service reset plan\n'
printf '  service:              %s\n' "$SERVICE_NAME"
printf '  database provider:    %s\n' "$PROVIDER"
printf '  app containers/images: selected by Kamal service labels\n'
if (( ${#ACCESSORY_CONTAINERS[@]} )); then
  printf '  accessory containers: %s\n' "${ACCESSORY_CONTAINERS[*]}"
else
  printf '  accessory containers: none declared by any destination\n'
fi
printf '  app directories:      %s\n' "$(printf '~/.kamal/apps/%s ' "${APP_DIRECTORIES[@]}" | sed 's/ $//')"
printf '  proxy services:       %s\n' "${PROXY_SERVICES[*]}"
printf '  persistent state:     %s\n' "$REMOTE_ROOT"
printf '  preserved:            shared proxy/network, unrelated services, GitHub secrets, Stripe\n'

if [[ "$CONFIRMED" != "true" ]]; then
  printf '\nPreview only. Re-run with --yes to permanently remove these targets.\n'
  exit 0
fi

if ! command -v kamal >/dev/null 2>&1; then
  printf 'Refusing reset: kamal is not installed or not on PATH.\n' >&2
  exit 2
fi

# Kamal renders config/deploy.yml through ERB before it does anything, so a missing
# variable surfaces as an obscure failure partway through the reset. Fail first instead.
MISSING_ENV=()
for name in IMAGE KAMAL_DEPLOY_IP KAMAL_DEPLOY_HOST KAMAL_REGISTRY_USERNAME KAMAL_REGISTRY_PASSWORD; do
  [[ -n "${!name:-}" ]] || MISSING_ENV+=("$name")
done
if (( ${#MISSING_ENV[@]} )); then
  printf 'Refusing reset: the deploy environment is incomplete. Missing: %s\n' "${MISSING_ENV[*]}" >&2
  printf 'Use the same environment as a normal Kamal deployment, including SSH access.\n' >&2
  exit 2
fi

EXPECTED_ROOT="/opt/docker/$SERVICE_NAME"
if [[ "$REMOTE_ROOT" != "$EXPECTED_ROOT" || "$REMOTE_ROOT" == "/opt/docker" ]]; then
  printf 'Refusing reset: target validation failed.\n' >&2
  exit 2
fi

cd "$PROJECT_ROOT"
export SERVICE="$SERVICE_NAME"

# Kamal scopes container names, container labels, and image labels by destination:
# `-d sqlite` only ever matches containers named next-saas-web-sqlite-* and labelled
# destination=sqlite. A deployment made under a different destination, or under none at
# all before this repository used destinations, is invisible to it. Sweep every scope so
# the reset is complete whichever way the previous release was deployed. The empty scope
# is the legacy no-destination deployment.
# Order matters. Without -d, Kamal's label filters omit destination= and therefore stop
# every destination's containers, and it only deregisters a proxy service while that
# service's container is still running. Run the specific destinations first so each one
# deregisters itself, and leave the unfiltered legacy scope for last.
KAMAL_SCOPES=()
for destination in "${DESTINATIONS[@]}"; do
  KAMAL_SCOPES+=("$destination")
done
KAMAL_SCOPES+=("")

for scope in "${KAMAL_SCOPES[@]}"; do
  scope_args=()
  [[ -n "$scope" ]] && scope_args=(-d "$scope")
  label="${scope:-no destination}"

  # A failed earlier deploy can leave a lock held, which would block removal.
  printf '\nReleasing any held Kamal lock (%s)...\n' "$label"
  kamal lock release "${scope_args[@]}" || true

  printf 'Removing application containers, images, and proxy registration (%s)...\n' "$label"
  kamal app remove "${scope_args[@]}" || \
    printf '  no %s deployment to remove\n' "$label"
done

VERIFY="test ! -e '$REMOTE_ROOT' && test -z \"\$(docker ps -aq --filter label=service='$SERVICE_NAME')\""

# `kamal accessory remove` uses `docker container prune`, which skips running containers,
# so remove them directly instead.
for container in ${ACCESSORY_CONTAINERS[@]+"${ACCESSORY_CONTAINERS[@]}"}; do
  printf 'Removing the %s accessory container when present...\n' "$container"
  kamal server exec --no-interactive -d "$PROVIDER" \
    "docker container rm --force '$container' >/dev/null 2>&1 || true"
  VERIFY+=" && test -z \"\$(docker ps -aq --filter name='^/$container\$')\""

  # Kamal uploads an accessory's `files:` to ~/<accessory-service-name>/ on the host and
  # only removes that directory through `kamal accessory remove`, which this script does
  # not use. Its own env files live under the destination app directory removed below.
  kamal server exec --no-interactive -d "$PROVIDER" \
    "rm -rf -- \"\$HOME/$container\""
  VERIFY+=" && test ! -e \"\$HOME/$container\""
done

# Kamal's own removal is destination-filtered, so anything deployed under a scope this
# repository no longer defines would survive. This exact-label sweep guarantees the clean
# slate. The label is matched exactly, so a service such as "next-static" is never touched.
# Kamal only deregisters a proxy service when it finds a running container for it, so a
# route can outlive the container and leave the host returning 502 or, worse, collide with
# the next deployment under a different destination. Remove each by name regardless.
printf 'Removing kamal-proxy registrations...\n'
for proxy_service in "${PROXY_SERVICES[@]}"; do
  kamal server exec --no-interactive -d "$PROVIDER" \
    "docker exec kamal-proxy kamal-proxy remove '$proxy_service' >/dev/null 2>&1 || true"
done
# Kamal wraps this whole string in single quotes for the remote shell, so the check must
# contain no single quotes and no shell metacharacters. The service name is already
# restricted to [a-z0-9-], which makes a fixed-string grep safe unquoted.
VERIFY+=" && test -z \"\$(docker exec kamal-proxy kamal-proxy list 2>/dev/null | grep -F -- $SERVICE_NAME- || true)\""

printf 'Sweeping any remaining containers and images labelled service=%s...\n' "$SERVICE_NAME"
kamal server exec --no-interactive -d "$PROVIDER" \
  "docker ps -aq --filter label=service='$SERVICE_NAME' | xargs -r docker rm --force >/dev/null 2>&1 || true"
kamal server exec --no-interactive -d "$PROVIDER" \
  "docker image prune --all --force --filter label=service='$SERVICE_NAME' >/dev/null 2>&1 || true"
VERIFY+=" && test -z \"\$(docker images -q --filter label=service='$SERVICE_NAME')\""

printf 'Removing Kamal app directories for every destination...\n'
for directory in "${APP_DIRECTORIES[@]}"; do
  kamal server exec --no-interactive -d "$PROVIDER" \
    "rm -rf -- \"\$HOME/.kamal/apps/$directory\""
  VERIFY+=" && test ! -e \"\$HOME/.kamal/apps/$directory\""
done

printf 'Removing service-owned persistent state...\n'
kamal server exec --no-interactive -d "$PROVIDER" \
  "rm -rf -- '$REMOTE_ROOT'"

printf 'Verifying reset...\n'
if ! kamal server exec --no-interactive -d "$PROVIDER" "$VERIFY"; then
  printf 'Reset verification failed: some containers or state still exist on the host.\n' >&2
  printf 'Inspect the host before deploying; the service is in a partially reset state.\n' >&2
  exit 1
fi

printf 'Kamal service %s was reset. The shared proxy and unrelated deployments were preserved.\n' "$SERVICE_NAME"
