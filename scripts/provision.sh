#!/usr/bin/env bash
# One of a release's two deployments of infra/main.bicep (D-205):
#
#   scripts/provision.sh without-app   everything the app needs, including the migration job on the release's image,
#                                      while the app that is serving is left alone
#   scripts/provision.sh with-app      the same again, and the app itself on the release's image
#
# The schema is migrated between the two (scripts/migrate.sh). The deployment workflow sets every input in the
# environment; secrets go to the Azure CLI in a parameters file only this process can read, never on a command line.
set -euo pipefail

case "${1:-}" in
  without-app) deploy_app=false ;;
  with-app) deploy_app=true ;;
  *)
    echo "usage: provision.sh without-app|with-app" >&2
    exit 2
    ;;
esac

parameters=$(umask 077 && mktemp)
trap 'rm -f "$parameters"' EXIT

DEPLOY_APP=$deploy_app jq -n '{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  contentVersion: "1.0.0.0",
  parameters: {
    namePrefix: { value: env.NAME_PREFIX },
    location: { value: env.LOCATION },
    image: { value: env.IMAGE },
    registryName: { value: env.REGISTRY_NAME },
    registryResourceGroup: { value: env.REGISTRY_RESOURCE_GROUP },
    deployApp: { value: (env.DEPLOY_APP == "true") },
    adminHost: { value: (env.ADMIN_HOST // "") },
    storeHostSuffix: { value: env.STORE_HOST_SUFFIX },
    databasePassword: { value: env.DATABASE_PASSWORD },
    stripeSecretKey: { value: (env.STRIPE_SECRET_KEY // "") },
    stripeWebhookSecret: { value: (env.STRIPE_WEBHOOK_SECRET // "") },
    emailProvider: { value: env.EMAIL_PROVIDER },
    emailSenderAddress: { value: env.EMAIL_SENDER_ADDRESS },
    mailgunDomain: { value: env.MAILGUN_DOMAIN },
    mailgunApiKey: { value: env.MAILGUN_API_KEY },
    mailgunWebhookSigningKey: { value: env.MAILGUN_WEBHOOK_SIGNING_KEY }
  }
}' >"$parameters"

az deployment sub create \
  --name "$NAME_PREFIX-$1" \
  --location "$LOCATION" \
  --template-file infra/main.bicep \
  --parameters @"$parameters"
