using 'main.bicep'

param namePrefix = 'shopforge-prod'
param location = 'westeurope'
param image = 'shopforgeprod.azurecr.io/shopforge-api:latest'
param registryServer = 'shopforgeprod.azurecr.io'
param storefrontFqdn = 'shopforgeprodmedia.z6.web.core.windows.net'
param databasePassword = readEnvironmentVariable('SHOPFORGE_DB_PASSWORD')
param stripeSecretKey = readEnvironmentVariable('SHOPFORGE_STRIPE_SECRET_KEY', '')
param stripeWebhookSecret = readEnvironmentVariable('SHOPFORGE_STRIPE_WEBHOOK_SECRET', '')
param emailProvider = readEnvironmentVariable('SHOPFORGE_EMAIL_PROVIDER', 'log')
param emailSenderAddress = readEnvironmentVariable('SHOPFORGE_EMAIL_SENDER', '')
param mailgunDomain = readEnvironmentVariable('SHOPFORGE_MAILGUN_DOMAIN', '')
param mailgunApiKey = readEnvironmentVariable('SHOPFORGE_MAILGUN_API_KEY', '')
