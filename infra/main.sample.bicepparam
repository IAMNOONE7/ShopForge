using 'main.bicep'

param namePrefix = 'shopforge-prod'
param location = 'westeurope'
param image = 'shopforgeprod.azurecr.io/shopforge-api:latest'
param registryName = 'shopforgeprod'
param registryResourceGroup = 'shopforge-shared-rg'
param adminHost = 'admin.example.cz'
param storeHostSuffix = 'shops.example.cz'
param databasePassword = readEnvironmentVariable('SHOPFORGE_DB_PASSWORD')
param stripeSecretKey = readEnvironmentVariable('SHOPFORGE_STRIPE_SECRET_KEY', '')
param stripeWebhookSecret = readEnvironmentVariable('SHOPFORGE_STRIPE_WEBHOOK_SECRET', '')
param emailSenderAddress = readEnvironmentVariable('SHOPFORGE_EMAIL_SENDER')
param mailgunDomain = readEnvironmentVariable('SHOPFORGE_MAILGUN_DOMAIN')
param mailgunApiKey = readEnvironmentVariable('SHOPFORGE_MAILGUN_API_KEY')
param mailgunWebhookSigningKey = readEnvironmentVariable('SHOPFORGE_MAILGUN_WEBHOOK_SIGNING_KEY')
