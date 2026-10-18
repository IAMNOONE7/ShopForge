#!/usr/bin/env bash
# Checks that a deployment serves what it deployed before anybody is told it succeeded (D-125, D-205). Each kind of
# check runs against what the release has just touched:
#
#   scripts/smoke.sh --api https://api.example.com [--version <commit>] [--store-host shop.example.com]
#       the API is alive and ready, is the build that was released, and resolves a published store
#
#   scripts/smoke.sh --site https://www.example.com/ [--admin https://admin.example.com/admin/orders]
#                    [--http http://www.example.com/] [--store https://shop.example.com/] [--origin https://<app>]
#       the storefront's page and its bundle, the security headers it is served with, an admin deep link, plain
#       HTTP sent on to HTTPS, a store's page with the data it renders from, and the app itself refusing a request
#       that did not come through the edge
#
# Every check runs, so one run reports everything that is wrong.
set -euo pipefail

api='' version='' store_host='' site='' admin='' http='' store='' origin=''

while [ "$#" -gt 0 ]; do
  case "$1" in
    --api) api=$2 ;;
    --version) version=$2 ;;
    --store-host) store_host=$2 ;;
    --site) site=$2 ;;
    --admin) admin=$2 ;;
    --http) http=$2 ;;
    --store) store=$2 ;;
    --origin) origin=$2 ;;
    *)
      echo "smoke.sh: unknown option $1" >&2
      exit 2
      ;;
  esac
  shift 2
done

if [ -z "$api" ] && [ -z "$site" ] && [ -z "$origin" ]; then
  echo "usage: smoke.sh --api <url> [--version <commit>] [--store-host <host>] | --site <url> [--admin <url>] [--http <url>] [--store <url>] [--origin <url>]" >&2
  exit 2
fi

scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
failures=0

ok() { echo "  ok       $1"; }

failed() {
  echo "  FAILED   $1"
  failures=$((failures + 1))
}

# Fetches a URL without following redirects. Prints the status, and leaves the headers and the body in the scratch
# directory. Not being able to connect at all is a status of 000 and counts as a failure, not the end of the run.
fetch() {
  local url=$1 status
  shift
  : >"$scratch/body"
  : >"$scratch/headers"
  status=$(curl -s -o "$scratch/body" -D "$scratch/headers" -w '%{http_code}' --max-time 20 "$@" "$url" || true)
  echo "${status:-000}"
}

header() { grep -i "^$1:" "$scratch/headers" | tail -n 1 | cut -d: -f2- | tr -d '\r' | sed 's/^ *//' || true; }

check() {
  local name=$1 expected=$2 url=$3 status
  shift 3
  status=$(fetch "$url" "$@")

  if [ "$status" = "$expected" ]; then
    ok "$name ($status)"
  else
    failed "$name (expected $expected, got $status)"
  fi
}

# A built single-page app: an HTML page with the root the app mounts on.
is_app_page() { [ "$1" = 200 ] && header Content-Type | grep -qi 'text/html' && grep -q '<div id="root">' "$scratch/body"; }

if [ -n "$api" ]; then
  echo "API checks against $api"

  # Alive says the process is up; ready says it can reach the database and its storage, and that the database has
  # every migration the build carries.
  check "health/live" 200 "$api/health/live"
  check "health/ready" 200 "$api/health/ready"

  # An unknown host must not be served by somebody else's store (D-124).
  check "unknown host is not served" 404 "$api/api/storefront/store" -H "Host: not-a-store.invalid"

  # Signing in with nothing should be refused rather than fail: it proves the request pipeline runs end to end.
  check "admin sign-in refuses an empty request" 401 "$api/api/admin/auth/login" \
    -X POST -H 'Content-Type: application/json' -d '{"email":"","password":""}'

  # A check that passes against the revision being replaced proves nothing, so the build has to be the release's.
  if [ -n "$version" ]; then
    status=$(fetch "$api/health/version")
    answered=$(head -c 120 "$scratch/body")

    if [ "$status" = 200 ] && grep -qF -- "$version" "$scratch/body"; then
      ok "the released build answers ($answered)"
    else
      failed "the released build answers (expected $version, got $status: $answered)"
    fi
  fi

  if [ -n "$store_host" ]; then
    check "store $store_host is resolved" 200 "$api/api/storefront/store" -H "Host: $store_host"
  fi
fi

if [ -n "$site" ]; then
  echo "Site checks against $site"
  site_origin=$(printf '%s' "$site" | sed -E 's#^(https?://[^/]+).*#\1#')
  status=$(fetch "$site")

  if is_app_page "$status"; then
    ok "the storefront's page ($status)"
  else
    failed "the storefront's page (expected the built app's HTML, got $status $(header Content-Type))"
  fi

  cp "$scratch/headers" "$scratch/site-headers"
  bundle=$(grep -oE 'src="/assets/[^"]+\.js"' "$scratch/body" | head -n 1 | sed -E 's/^src="|"$//g' || true)

  # A plain HTTP site cannot promise HTTPS, so the promise is looked for only where there is HTTPS to promise.
  for name in Strict-Transport-Security Content-Security-Policy Referrer-Policy; do
    if [ "$name" = Strict-Transport-Security ] && [[ "$site" != https://* ]]; then
      echo "  skipped  header $name (the site is plain HTTP)"
    elif grep -qi "^$name:" "$scratch/site-headers"; then
      ok "header $name"
    else
      failed "header $name is missing"
    fi
  done

  if grep -qi '^X-Content-Type-Options: *nosniff' "$scratch/site-headers"; then
    ok "header X-Content-Type-Options"
  else
    failed "header X-Content-Type-Options is not nosniff"
  fi

  if grep -qi '^X-Frame-Options:' "$scratch/site-headers" || grep -i '^Content-Security-Policy:' "$scratch/site-headers" | grep -qi 'frame-ancestors'; then
    ok "framing is refused"
  else
    failed "framing is refused (no X-Frame-Options and no frame-ancestors)"
  fi

  # A fallback that answers every path with the page would answer the bundle with HTML too, and a browser runs none of it.
  if [ -z "$bundle" ]; then
    failed "the storefront's bundle (the page names none)"
  else
    status=$(fetch "$site_origin$bundle")

    if [ "$status" = 200 ] && header Content-Type | grep -qiE 'javascript|ecmascript'; then
      ok "the storefront's bundle $bundle ($status $(header Content-Type))"
    else
      failed "the storefront's bundle $bundle (expected JavaScript, got $status $(header Content-Type))"
    fi
  fi

  if [ -n "$admin" ]; then
    status=$(fetch "$admin")

    if is_app_page "$status" && grep -q '/admin/assets/' "$scratch/body"; then
      ok "an admin deep link ($status)"
    else
      failed "an admin deep link (expected the admin's page, got $status: $(grep -oE 'src="[^"]+\.js"' "$scratch/body" | head -n 1 || true))"
    fi
  fi

  if [ -n "$http" ]; then
    status=$(fetch "$http")
    location=$(header Location)

    case "$status" in
      301 | 302 | 307 | 308)
        if [[ "$location" == https://* ]]; then
          ok "plain HTTP is sent on to HTTPS ($status)"
        else
          failed "plain HTTP is sent on to HTTPS ($status to '$location')"
        fi
        ;;
      *) failed "plain HTTP is sent on to HTTPS (expected a redirect, got $status)" ;;
    esac
  fi

  if [ -n "$store" ]; then
    status=$(fetch "$store")

    if is_app_page "$status"; then
      ok "a store's page ($status)"
    else
      failed "a store's page (expected the built app's HTML, got $status)"
    fi

    status=$(fetch "${store%/}/api/storefront/store")

    if [ "$status" = 200 ] && header Content-Type | grep -qi 'application/json' && grep -q '"name"' "$scratch/body"; then
      ok "the store's data behind its page ($status)"
    else
      failed "the store's data behind its page (expected the store as JSON, got $status)"
    fi
  fi
fi

# The app answers only its edge, so a request straight to it is refused; the platform still probes its health (D-207).
if [ -n "$origin" ]; then
  echo "Origin checks against $origin"
  check "a request around the edge is refused" 403 "$origin/api/storefront/store"
  check "the platform can still probe it" 200 "$origin/health/live"
fi

if [ "$failures" -gt 0 ]; then
  echo "$failures smoke check(s) failed."
  exit 1
fi

echo "All smoke checks passed."
