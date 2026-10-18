#!/usr/bin/env bash
# One of a release's two deployments of an environment (D-205, D-208):
#
#   scripts/provision.sh <environment> without-app   everything the app needs, including the migration job on the
#                                                    release's image, while the app that is serving is left alone
#   scripts/provision.sh <environment> with-app      the same again, and the app itself on the release's image
#
# The environment is infra/environments/<environment>.bicepparam. Its name and sizes are in the file; host names, mail,
# the image and every secret are handed to the Azure CLI in its own environment, which it reads as it compiles the
# file. No secret is ever on a command line or written to disk.
set -euo pipefail

environment=${1:-}
which=${2:-}

case "$which" in
  without-app) deploy_app=false ;;
  with-app) deploy_app=true ;;
  *)
    echo "usage: provision.sh <environment> without-app|with-app" >&2
    exit 2
    ;;
esac

parameters="infra/environments/$environment.bicepparam"

if [ -z "$environment" ] || [ ! -f "$parameters" ]; then
  echo "There is no environment called '$environment' (no $parameters)." >&2
  exit 2
fi

SHOPFORGE_DEPLOY_APP=$deploy_app \
SHOPFORGE_IMAGE=$IMAGE \
SHOPFORGE_LOCATION=$LOCATION \
SHOPFORGE_REGISTRY_NAME=$REGISTRY_NAME \
SHOPFORGE_REGISTRY_RESOURCE_GROUP=$REGISTRY_RESOURCE_GROUP \
SHOPFORGE_STORE_HOST_SUFFIX=$STORE_HOST_SUFFIX \
SHOPFORGE_ADMIN_HOST=${ADMIN_HOST:-} \
SHOPFORGE_FIRST_OPERATOR=${FIRST_OPERATOR_EMAIL:-} \
SHOPFORGE_EMAIL_PROVIDER=$EMAIL_PROVIDER \
SHOPFORGE_EMAIL_SENDER=$EMAIL_SENDER_ADDRESS \
SHOPFORGE_MAILGUN_DOMAIN=$MAILGUN_DOMAIN \
SHOPFORGE_DB_PASSWORD=$DATABASE_PASSWORD \
SHOPFORGE_MAILGUN_API_KEY=$MAILGUN_API_KEY \
SHOPFORGE_MAILGUN_WEBHOOK_SIGNING_KEY=$MAILGUN_WEBHOOK_SIGNING_KEY \
SHOPFORGE_STRIPE_SECRET_KEY=${STRIPE_SECRET_KEY:-} \
SHOPFORGE_STRIPE_WEBHOOK_SECRET=${STRIPE_WEBHOOK_SECRET:-} \
  az deployment sub create \
    --name "shopforge-$environment-$which" \
    --location "$LOCATION" \
    --parameters "$parameters"
