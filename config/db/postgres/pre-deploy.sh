#!/usr/bin/env bash
# Provider pre-deploy hook for the `postgres` destination.
#
# Run by the Release workflow as config/db/$DB_PROVIDER/pre-deploy.sh before `kamal deploy`.
# Providers without a database server (sqlite) simply have no hook file.
#
# Boots the accessory on the first release and starts it on every release afterwards,
# so a restarted or stopped host never deploys the application against a missing database.
set -euo pipefail

: "${SERVICE:?SERVICE must be set}"

if kamal server exec --no-interactive -d postgres \
  "docker inspect ${SERVICE}-postgres" >/dev/null 2>&1; then
  echo "PostgreSQL accessory ${SERVICE}-postgres exists; ensuring it is started"
  kamal accessory start postgres -d postgres
else
  echo "PostgreSQL accessory ${SERVICE}-postgres not found; booting it"
  kamal accessory boot postgres -d postgres
fi
