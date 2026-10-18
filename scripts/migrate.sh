#!/usr/bin/env bash
# Runs a release's migration job and waits for it to end: the schema is migrated by the release's own image, inside
# its environment, before the app is told to run that image (D-205). While the platform has nobody, the same job invites
# the first operator the environment names (D-208).
#
#   scripts/migrate.sh <environment>
#
# The job and its resource group are what the release's first deployment says they are (D-208).
# On a first deployment the job can start before the roles it was just granted have reached the vault, and fail to
# read its settings; so a failed run is tried again, twice, a minute apart. A migration that fails three times fails
# the release, and the app is never touched.
set -euo pipefail

environment=${1:?"usage: migrate.sh <environment>"}
outputs=$(az deployment sub show --name "shopforge-$environment-without-app" --query properties.outputs --output json)
group=$(jq -r .resourceGroup.value <<<"$outputs")
job=$(jq -r .migrationJob.value <<<"$outputs")
attempts=3
pause=${MIGRATION_RETRY_PAUSE_SECONDS:-60}
poll=${MIGRATION_POLL_SECONDS:-10}

for attempt in $(seq 1 "$attempts"); do
  execution=$(az containerapp job start --resource-group "$group" --name "$job" --query name --output tsv)
  echo "Migration $execution started (attempt $attempt of $attempts)."

  while true; do
    status=$(az containerapp job execution show --resource-group "$group" --name "$job" \
      --job-execution-name "$execution" --query properties.status --output tsv)

    case "$status" in
      Succeeded)
        echo "Migration $execution succeeded."
        exit 0
        ;;
      Failed | Stopped | Degraded)
        echo "Migration $execution ended $status."
        break
        ;;
      *)
        sleep "$poll"
        ;;
    esac
  done

  # What the build said about it, if the CLI can fetch it; the job's logs are in Log Analytics either way.
  az containerapp job logs show --resource-group "$group" --name "$job" --execution "$execution" \
    --container migrate --format text 2>/dev/null || true

  if [ "$attempt" -lt "$attempts" ]; then
    sleep "$pause"
  fi
done

echo "The migration job failed, so the app was left on the image it was serving."
exit 1
