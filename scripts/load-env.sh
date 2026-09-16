#!/usr/bin/env bash
# Shared .env loader for the operator scripts.
#
# config/deploy.yml loads .env through ERB with Dotenv.load, which never overwrites a
# variable already present in the environment. Sourcing the file with `set -a` does the
# opposite, silently replacing values passed explicitly on the command line: a DB_PASSWORD
# or DB_PROVIDER exported for one run would be overridden by a stale .env. Match Dotenv.
load_env_file() {
  local file="$1" line key value
  [[ -f "$file" ]] || return 0
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ "$line" =~ ^[[:space:]]*(#|$) ]] && continue
    key="${line%%=*}"
    key="${key#"${key%%[![:space:]]*}"}"
    key="${key%"${key##*[![:space:]]}"}"
    [[ "$key" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || continue
    # An existing environment variable always wins.
    [[ -n "${!key:-}" ]] && continue
    value="${line#*=}"
    value="${value#"${value%%[![:space:]]*}"}"
    value="${value%"${value##*[![:space:]]}"}"
    if [[ ${#value} -ge 2 && ( ( "$value" == \"*\" ) || ( "$value" == \'*\' ) ) ]]; then
      value="${value:1:${#value}-2}"
    fi
    printf -v "$key" '%s' "$value"
    export "$key"
  done < "$file"
}
