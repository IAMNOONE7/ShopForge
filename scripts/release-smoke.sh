#!/usr/bin/env bash
# The smoke tests of a release, pointed at what its deployment says it made (D-125, D-205, D-208):
#
#   scripts/release-smoke.sh <environment> <commit> api    the API answers through the edge, as the release's build
#   scripts/release-smoke.sh <environment> <commit> site   the storefront and admin pages through the edge, plain HTTP
#                                                          sent on to HTTPS, the app refusing anybody who goes around
#                                                          the edge, and a published store when SMOKE_STORE_HOST names one
#
# Every address comes from the deployment's outputs, so a brand-new environment is tested before it has a shop or a
# domain of its own. The admin is checked on the edge's own host name, where it is served whatever the admin's domain
# is doing (D-208).
set -euo pipefail

environment=${1:?"usage: release-smoke.sh <environment> <commit> api|site"}
commit=${2:?"usage: release-smoke.sh <environment> <commit> api|site"}
outputs=$(az deployment sub show --name "shopforge-$environment-with-app" --query properties.outputs --output json)
edge=$(jq -r .publicHostName.value <<<"$outputs")
origin=$(jq -r .apiFqdn.value <<<"$outputs")
smoke=${SMOKE_SCRIPT:-$(dirname "$0")/smoke.sh}

case "${3:-}" in
  api)
    exec "$smoke" --api "https://$edge" --version "$commit"
    ;;
  site)
    store=()

    if [ -n "${SMOKE_STORE_HOST:-}" ]; then
      store=(--store "https://$SMOKE_STORE_HOST/")
    fi

    exec "$smoke" --site "https://$edge/" --admin "https://$edge/admin/orders" \
      --http "http://$edge/" --origin "https://$origin" "${store[@]}"
    ;;
  *)
    echo "usage: release-smoke.sh <environment> <commit> api|site" >&2
    exit 2
    ;;
esac
