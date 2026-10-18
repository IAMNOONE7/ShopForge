#!/usr/bin/env bash
# Rehearses a release on this machine the way the deployment runs one, against a throwaway PostgreSQL and Azurite
# (D-205). Two builds cross a real migration: the previous one, and the release's, which is the working tree.
#
#   tests/release/rehearse.sh [<previous-ref>]
#
#   1. The previous build serves, at its own schema.
#   2. The order the deployment used to have: the release's build starts before the migration. It serves nothing but
#      its health and does no background work.
#   3. The release's build migrates the schema, as the release's job does, while the previous build keeps serving.
#   4. The release's build catches up without a restart and passes the smoke test as itself; the previous one stops.
#      With the frontends built in the source tree (npm run build in frontend/storefront and frontend/admin), it
#      serves them too, and passes the site's smoke test (D-206).
#   5. Rollback: the previous build serves again, against the newer schema, and passes the smoke test as itself.
#
# The previous build defaults to the commit before the newest migration arrived, so the rehearsal always crosses one.
# Mail is pointed where nothing listens, so nothing leaves this machine.
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/../.." && pwd)
STATE="$ROOT/.release-rehearsal"
COMPOSE=(docker compose -f "$ROOT/tests/release/docker-compose.yml")
PREVIOUS_URL=http://127.0.0.1:5281
RELEASE_URL=http://127.0.0.1:5282
STORE_HOST=shop-a.localhost

newest=$(git -C "$ROOT" log -n 1 --format=%H -- src/ShopForge.Infrastructure/Persistence/Migrations)
previous=$(git -C "$ROOT" rev-parse "${1:-$newest^}^{commit}")
release=$(git -C "$ROOT" rev-parse HEAD)

settings=(
  "ConnectionStrings__ShopForge=Host=127.0.0.1;Port=5436;Database=shopforge;Username=shopforge;Password=rehearsal"
  "ConnectionStrings__FileStorage=DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10012/devstoreaccount1;"
  "StoreHosts__Suffix=localhost"
  "Email__Provider=mailgun"
  "Email__SenderAddress=rehearsal@mail.rehearsal.invalid"
  "Email__Mailgun__ApiKey=rehearsal"
  "Email__Mailgun__Domain=mail.rehearsal.invalid"
  "Email__Mailgun__BaseUrl=http://127.0.0.1:9"
  # The rehearsal asks the shop several times a second; it is one caller, and what it measures is not the limits.
  "RateLimiting__Writes__PermitLimit=100000"
  "RateLimiting__Global__PermitLimit=1000000"
)

failures=0
pids=()

pass() { echo "  PASS  $1"; }

fail() {
  echo "  FAIL  $1"
  failures=$((failures + 1))
}

status_of() { curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$@" || true; }

expect() {
  local name=$1 expected=$2 actual=$3

  if [ "$actual" = "$expected" ]; then
    pass "$name ($actual)"
  else
    fail "$name (expected $expected, got $actual)"
  fi
}

cleanup() {
  for pid in "${pids[@]}"; do
    kill "$pid" 2>/dev/null || true
  done

  "${COMPOSE[@]}" down --volumes >/dev/null 2>&1 || true
}

trap cleanup EXIT

# Starts a build in the given environment on the given address, from its own folder as the container runs it, and
# remembers it by name.
start() {
  local name=$1 app=$2 environment=$3 url=$4

  (cd "$app" && exec env "${settings[@]}" ASPNETCORE_ENVIRONMENT="$environment" ASPNETCORE_URLS="$url" \
    dotnet ShopForge.Api.dll >>"$STATE/$name.log" 2>&1) &
  echo $! >"$STATE/$name.pid"
  pids+=("$!")
}

stop() {
  local pid
  pid=$(cat "$STATE/$1.pid")
  kill "$pid" 2>/dev/null || true

  while kill -0 "$pid" 2>/dev/null; do
    sleep 0.2
  done
}

# Waits until a URL answers with the status wanted, for up to a minute.
wait_for() {
  local url=$1 wanted=$2

  for _ in $(seq 1 300); do
    if [ "$(status_of "$url")" = "$wanted" ]; then
      return 0
    fi

    sleep 0.2
  done

  return 1
}

publish() {
  local name=$1 source=$2 version=$3
  dotnet publish "$source/src/ShopForge.Api/ShopForge.Api.csproj" --configuration Release --output "$STATE/$name" \
    -p:SourceRevisionId="$version" -v q -nologo >"$STATE/$name-publish.log" 2>&1 \
    || { echo "The $name build failed; see $STATE/$name-publish.log" >&2; exit 1; }
}

rm -rf "$STATE"
mkdir -p "$STATE/previous-source"

echo "Rehearsing the release of $release over $previous."
git -C "$ROOT" archive "$previous" | tar -x -C "$STATE/previous-source"
publish previous "$STATE/previous-source" "$previous"
publish release "$ROOT" "$release"

# The image builds the frontends itself; here they are taken as the source tree last built them, if it did.
frontends=false

if [ -f "$ROOT/frontend/storefront/dist/index.html" ] && [ -f "$ROOT/frontend/admin/dist/index.html" ]; then
  mkdir -p "$STATE/release/frontends"
  cp -R "$ROOT/frontend/storefront/dist" "$STATE/release/frontends/storefront"
  cp -R "$ROOT/frontend/admin/dist" "$STATE/release/frontends/admin"
  frontends=true
fi
"${COMPOSE[@]}" up -d --wait >/dev/null 2>&1

# The previous release, as it stood: its own schema, and a demo shop to serve. Development is what migrates and
# seeds; it is stopped and started again as a deployment runs it.
echo "1. The previous build serves at its own schema."
start seed "$STATE/previous" Development "$PREVIOUS_URL"
wait_for "$PREVIOUS_URL/health/ready" 200 || { echo "The previous build did not seed its schema; see $STATE/seed.log" >&2; exit 1; }
stop seed
start previous "$STATE/previous" Production "$PREVIOUS_URL"
wait_for "$PREVIOUS_URL/health/ready" 200 || fail "the previous build became ready"
expect "the previous build serves the shop" 200 "$(status_of -H "Host: $STORE_HOST" "$PREVIOUS_URL/api/storefront/products")"

echo "2. The release's build, started before the migration, as the deployment used to start it."
start release "$STATE/release" Production "$RELEASE_URL"
wait_for "$RELEASE_URL/health/live" 200 || fail "the release's build started"
sleep 4
expect "it is alive" 200 "$(status_of "$RELEASE_URL/health/live")"
expect "it is not ready" 503 "$(status_of "$RELEASE_URL/health/ready")"
expect "it serves nothing else" 503 "$(status_of -H "Host: $STORE_HOST" "$RELEASE_URL/api/storefront/products")"

if grep -q 'migration(s) behind this build' "$STATE/release.log"; then
  pass "it says what it is waiting for"
else
  fail "it says what it is waiting for"
fi

if grep -qE 'does not exist|pass failed|outbox run failed' "$STATE/release.log"; then
  fail "it does no background work against the old schema"
else
  pass "it does no background work against the old schema"
fi

echo "3. The release's build migrates the schema while the previous build serves."
: >"$STATE/served-during-migration"
(
  while [ ! -f "$STATE/migrated" ]; do
    status_of -H "Host: $STORE_HOST" "$PREVIOUS_URL/api/storefront/products" >>"$STATE/served-during-migration"
    echo >>"$STATE/served-during-migration"
    sleep 0.1
  done
) &
watcher=$!

if (cd "$STATE/release" && env "${settings[@]}" ASPNETCORE_ENVIRONMENT=Production dotnet ShopForge.Api.dll migrate >"$STATE/migrate.log" 2>&1); then
  pass "the migration ran ($(grep -o 'Applied [0-9]* migration(s)[^"]*' "$STATE/migrate.log" | head -n 1))"
else
  fail "the migration ran (see $STATE/migrate.log)"
fi

touch "$STATE/migrated"
wait "$watcher"
answers=$(grep -c . "$STATE/served-during-migration" || true)
refused=$(grep -vc '^200$' "$STATE/served-during-migration" || true)

if [ "$answers" -gt 0 ] && [ "$refused" = 0 ]; then
  pass "the previous build answered all $answers requests made during the migration"
else
  fail "the previous build answered every request made during the migration ($refused of $answers did not)"
fi

expect "the previous build serves the newer schema" 200 "$(status_of -H "Host: $STORE_HOST" "$PREVIOUS_URL/api/storefront/products")"

echo "4. The release's build catches up without a restart, and the previous build stops."

if wait_for "$RELEASE_URL/health/ready" 200; then
  pass "the release's build became ready"
else
  fail "the release's build became ready"
fi

expect "the release's build serves the shop" 200 "$(status_of -H "Host: $STORE_HOST" "$RELEASE_URL/api/storefront/products")"
"$ROOT/scripts/smoke.sh" --api "$RELEASE_URL" --version "$release" --store-host "$STORE_HOST" | sed 's/^/    /' \
  && pass "the smoke test, as the release's build" || fail "the smoke test, as the release's build"

if [ "$frontends" = true ]; then
  "$ROOT/scripts/smoke.sh" --site "http://$STORE_HOST:5282/" --admin "$RELEASE_URL/admin/orders" --store "http://$STORE_HOST:5282/" \
    | sed 's/^/    /' && pass "the site's smoke test, as the release's build" || fail "the site's smoke test, as the release's build"
else
  echo "  --    the site's smoke test is skipped: no frontends are built in $ROOT/frontend"
fi
stop previous

echo "5. Rollback: the previous build serves again, against the newer schema."
start rolled-back "$STATE/previous" Production "$PREVIOUS_URL"

if wait_for "$PREVIOUS_URL/health/ready" 200; then
  pass "the previous build became ready"
else
  fail "the previous build became ready"
fi

expect "the previous build serves the shop" 200 "$(status_of -H "Host: $STORE_HOST" "$PREVIOUS_URL/api/storefront/products")"

# A build from before the version endpoint is told apart by not having it: the release's build has.
version_status=$(status_of "$PREVIOUS_URL/health/version")

if [ "$version_status" = 404 ]; then
  pass "the answering build is the previous one (it predates /health/version)"
  smoke_version=()
else
  smoke_version=(--version "$previous")
fi

"$ROOT/scripts/smoke.sh" --api "$PREVIOUS_URL" "${smoke_version[@]}" --store-host "$STORE_HOST" | sed 's/^/    /' \
  && pass "the smoke test, as the previous build" || fail "the smoke test, as the previous build"

if [ "$failures" -gt 0 ]; then
  echo "$failures rehearsal check(s) failed. Logs are in $STATE."
  exit 1
fi

echo "The release rehearsal passed. Logs are in $STATE."
