#!/usr/bin/env bash
# The end-to-end stack: throwaway PostgreSQL and Azurite in compose, the API in its E2E environment, and the test rig
# beside it, all on this machine only (D-203).
#
#   tests/e2e/stack.sh up      builds, starts everything, waits until it answers, writes .e2e/stack.json
#   tests/e2e/stack.sh down    stops everything and throws the data away
#   tests/e2e/stack.sh status  says what is running
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/../.." && pwd)
STATE="$ROOT/.e2e"
COMPOSE=(docker compose -f "$ROOT/tests/e2e/docker-compose.yml")
API=http://127.0.0.1:5180
RIG=http://127.0.0.1:5190
OPERATOR=operator@e2e.test

started() { [[ -f "$STATE/$1.pid" ]] && kill -0 "$(cat "$STATE/$1.pid")" 2>/dev/null; }

wait_for() {
  local url=$1 name=$2
  for _ in $(seq 1 120); do
    curl -sf "$url" >/dev/null 2>&1 && return 0
    started "$name" || { echo "$name stopped while starting; see $STATE/$name.log" >&2; return 1; }
    sleep 1
  done
  echo "$name did not answer at $url within two minutes; see $STATE/$name.log" >&2
  return 1
}

start() {
  local name=$1 project=$2 url=$3
  shift 3
  # The environment is set here, not by a launch profile, so nothing can start it as Development.
  env ASPNETCORE_ENVIRONMENT=E2E ASPNETCORE_URLS="$url" DOTNET_ENVIRONMENT=E2E "$@" \
    nohup dotnet run --project "$ROOT/$project" --configuration Release --no-build --no-launch-profile \
    >"$STATE/$name.log" 2>&1 &
  echo $! >"$STATE/$name.pid"
}

up() {
  if started api || started rig; then
    echo "The stack is already up; tests/e2e/stack.sh down first." >&2
    exit 1
  fi

  mkdir -p "$STATE"
  "${COMPOSE[@]}" up -d --wait

  # The operator exists only in this stack's database, with a password made for this stack alone.
  local password
  password="E2e-$(od -An -tx1 -N18 /dev/urandom | tr -d ' \n')"

  dotnet build "$ROOT/src/ShopForge.Api/ShopForge.Api.csproj" --configuration Release -v q -nologo
  dotnet build "$ROOT/tests/ShopForge.TestRig/ShopForge.TestRig.csproj" --configuration Release -v q -nologo

  start api src/ShopForge.Api "$API" EndToEnd__Operator__Email="$OPERATOR" EndToEnd__Operator__Password="$password"
  wait_for "$API/health/ready" api
  start rig tests/ShopForge.TestRig "$RIG" Rig__Operator__Email="$OPERATOR" Rig__Operator__Password="$password"
  wait_for "$RIG/health" rig

  cat >"$STATE/stack.json" <<JSON
{
  "api": "$API",
  "rig": "$RIG",
  "operator": { "email": "$OPERATOR", "password": "$password" }
}
JSON
  echo "Up: API $API, rig $RIG. Addresses in $STATE/stack.json; start a run with: curl -X POST $RIG/runs"
}

down() {
  for name in rig api; do
    if started "$name"; then
      local pid
      pid=$(cat "$STATE/$name.pid")
      # `dotnet run` starts the program as a child; both go.
      pkill -P "$pid" 2>/dev/null || true
      kill "$pid" 2>/dev/null || true
    fi
    rm -f "$STATE/$name.pid"
  done

  "${COMPOSE[@]}" down --volumes --remove-orphans
  rm -f "$STATE/stack.json"
  echo "Down, and the data with it."
}

status() {
  for name in api rig; do
    if started "$name"; then echo "$name: running (pid $(cat "$STATE/$name.pid"))"; else echo "$name: stopped"; fi
  done
  "${COMPOSE[@]}" ps
}

case "${1:-}" in
  up) up ;;
  down) down ;;
  status) status ;;
  *) echo "usage: tests/e2e/stack.sh up|down|status" >&2; exit 2 ;;
esac
