#!/usr/bin/env bash
# Provider pre-deploy hook for the `sqlserver` destination.
#
# Run by the Release workflow as config/db/$DB_PROVIDER/pre-deploy.sh before `kamal deploy`.
#
# SQL Server's image runs no initializer scripts, so unlike PostgreSQL and MySQL the
# database and unprivileged login are created here, once the server accepts connections.
# Every step is idempotent, so a re-run on an existing deployment changes nothing.
set -euo pipefail

: "${SERVICE:?SERVICE must be set}"
: "${DB_PASSWORD:?DB_PASSWORD must be set}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONTAINER="${SERVICE}-sqlserver"
DATA_DIR="/opt/docker/${SERVICE}/mssql-2022"
TOOLS_IMAGE="mcr.microsoft.com/mssql-tools18"

# The image runs as uid 10001 and cannot write to a bind mount owned by root.
kamal server exec --no-interactive -d sqlserver \
  "mkdir -p '$DATA_DIR' && chown -R 10001:0 '$DATA_DIR'"

if kamal server exec --no-interactive -d sqlserver \
  "docker inspect $CONTAINER" >/dev/null 2>&1; then
  echo "SQL Server accessory $CONTAINER exists; ensuring it is started"
  kamal accessory start sqlserver -d sqlserver
else
  echo "SQL Server accessory $CONTAINER not found; booting it"
  kamal accessory boot sqlserver -d sqlserver
fi

# SQL Server takes appreciably longer than PostgreSQL or MySQL to accept connections, and
# creating the login before it is ready would fail the release for no real reason.
echo "Waiting for $CONTAINER to accept connections..."
kamal server exec --no-interactive -d sqlserver "$(cat <<REMOTE
set -e
for attempt in \$(seq 1 60); do
  if docker run --rm --network kamal $TOOLS_IMAGE \
      /opt/mssql-tools18/bin/sqlcmd -S '$CONTAINER' -U sa -P "\$MSSQL_SA_PASSWORD" -C -b \
      -Q 'SELECT 1' >/dev/null 2>&1; then
    echo "SQL Server is accepting connections after \$attempt attempt(s)"
    exit 0
  fi
  sleep 5
done
echo 'SQL Server did not accept connections within 300 seconds' >&2
exit 1
REMOTE
)" 2>&1 | grep -viE "^ *INFO|^App Host|^Running |^ *$" || true

echo "Ensuring the next_saas database and application login exist..."
kamal server exec --no-interactive -d sqlserver \
  "docker run --rm --network kamal -i $TOOLS_IMAGE \
     /opt/mssql-tools18/bin/sqlcmd -S '$CONTAINER' -U sa -P \"\$MSSQL_SA_PASSWORD\" -C -b \
     -v app_password=\"\$DB_PASSWORD\" -i /dev/stdin" < "$SCRIPT_DIR/init.sql"
