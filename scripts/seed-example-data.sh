#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if [[ "${ASPNETCORE_ENVIRONMENT:-Development}" != "Development" ]]; then
  echo "Refusing example-data seed: ASPNETCORE_ENVIRONMENT must be Development." >&2
  exit 2
fi

cd "$PROJECT_ROOT/MyApp"
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build --no-launch-profile --AppTasks=seed-example-data
