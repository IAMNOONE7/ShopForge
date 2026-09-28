#!/usr/bin/env bash
# Checks that a deployment is actually serving before anybody is told it succeeded. Run against the base URL of
# the environment that was just deployed:
#
#   scripts/smoke.sh https://api.example.com storefront.example.com
#
# The second argument is a host name the platform is expected to resolve to a published store; leave it out to
# check only the parts that need no data.
set -euo pipefail

base=${1:?"usage: smoke.sh <base-url> [storefront-host]"}
storefront=${2:-}
failures=0

check() {
  local name=$1 expected=$2
  shift 2
  # curl failing to connect at all must count as a failed check, not end the run: the point is to report
  # everything that is wrong, and "nothing answered" is the most important thing to report.
  local status
  status=$(curl -s -o /dev/null -w '%{http_code}' --max-time 20 "$@" || true)
  status=${status:-000}

  if [ "$status" = "$expected" ]; then
    echo "  ok       $name ($status)"
  else
    echo "  FAILED   $name (expected $expected, got $status)"
    failures=$((failures + 1))
  fi
}

echo "Smoke checks against $base"

# Alive says the process is up; ready says it can reach the database and its storage, which is what a deployment
# most often breaks.
check "health/live" 200 "$base/health/live"
check "health/ready" 200 "$base/health/ready"

# An unknown host must not be served by somebody else's store (D-124).
check "unknown host is not served" 404 -H "Host: not-a-store.invalid" "$base/api/storefront/store"

# Signing in with nothing should be refused rather than fail: it proves the request pipeline runs end to end.
check "admin sign-in refuses an empty request" 401 \
  -X POST -H 'Content-Type: application/json' -d '{"email":"","password":""}' "$base/api/admin/auth/login"

if [ -n "$storefront" ]; then
  check "storefront $storefront" 200 -H "Host: $storefront" "$base/api/storefront/store"
fi

if [ "$failures" -gt 0 ]; then
  echo "$failures smoke check(s) failed."
  exit 1
fi

echo "All smoke checks passed."
