#!/usr/bin/env bash
# Checks what a release is given before anything is built: every setting and secret the deployment passes on, by name
# only, and a mail provider that delivers. A release that would send no mail stops here, not after an hour of building
# (D-205). The deployment workflow runs it first, with the inputs in the environment.
set -euo pipefail

required=(
  NAME_PREFIX LOCATION REGISTRY_NAME REGISTRY_SERVER REGISTRY_RESOURCE_GROUP STORE_HOST_SUFFIX
  API_BASE_URL SITE_URL ADMIN_URL SMOKE_STORE_HOST DATABASE_PASSWORD EMAIL_PROVIDER EMAIL_SENDER_ADDRESS
  MAILGUN_DOMAIN MAILGUN_API_KEY MAILGUN_WEBHOOK_SIGNING_KEY
)
missing=()

for name in "${required[@]}"; do
  if [ -z "${!name:-}" ]; then
    missing+=("$name")
  fi
done

if [ "${#missing[@]}" -gt 0 ]; then
  echo "The release is missing: ${missing[*]}."
  exit 1
fi

provider=$(printf '%s' "$EMAIL_PROVIDER" | tr '[:upper:]' '[:lower:]')

if [ "$provider" != "mailgun" ]; then
  echo "EMAIL_PROVIDER is '$EMAIL_PROVIDER'. A deployment sends mail through mailgun; 'log' only writes it down and is for development."
  exit 1
fi

echo "The release has everything it needs."
