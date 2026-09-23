using 'main.bicep'

param namePrefix = 'shopforge-prod'
param location = 'westeurope'
param image = 'shopforgeprod.azurecr.io/shopforge-api:latest'
param registryServer = 'shopforgeprod.azurecr.io'
param storefrontFqdn = 'shopforgeprodmedia.z6.web.core.windows.net'
param databasePassword = readEnvironmentVariable('SHOPFORGE_DB_PASSWORD')
param stripeSecretKey = readEnvironmentVariable('SHOPFORGE_STRIPE_SECRET_KEY', '')
param stripeWebhookSecret = readEnvironmentVariable('SHOPFORGE_STRIPE_WEBHOOK_SECRET', '')
