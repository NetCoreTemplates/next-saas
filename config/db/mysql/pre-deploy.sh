#!/usr/bin/env bash
# Provider pre-deploy hook for the `mysql` destination.
#
# Run by the Release workflow as config/db/$DB_PROVIDER/pre-deploy.sh before `kamal deploy`.
# Boots the accessory on the first release and starts it on every release afterwards, so a
# restarted or stopped host never deploys the application against a missing database.
set -euo pipefail

: "${SERVICE:?SERVICE must be set}"

if kamal server exec --no-interactive -d mysql \
  "docker inspect ${SERVICE}-mysql" >/dev/null 2>&1; then
  echo "MySQL accessory ${SERVICE}-mysql exists; ensuring it is started"
  kamal accessory start mysql -d mysql
else
  echo "MySQL accessory ${SERVICE}-mysql not found; booting it"
  kamal accessory boot mysql -d mysql
fi
