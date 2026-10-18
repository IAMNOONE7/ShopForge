using '../main.bicep'

// What makes staging staging: its name and its sizes. Everything else is the GitHub environment's of the same name: host
// names, mail, the release's image and every secret, read when the deployment is compiled and never written here
// (D-208).
param namePrefix = 'shopforge-staging'
param databaseSkuName = 'Standard_B1ms'
param databaseTier = 'Burstable'
param databaseStorageGb = 32
param databaseBackupDays = 7
param appMinReplicas = 1
param appMaxReplicas = 2

param location = readEnvironmentVariable('SHOPFORGE_LOCATION', 'westeurope')
param image = readEnvironmentVariable('SHOPFORGE_IMAGE')
param deployApp = bool(readEnvironmentVariable('SHOPFORGE_DEPLOY_APP', 'true'))
param registryName = readEnvironmentVariable('SHOPFORGE_REGISTRY_NAME')
param registryResourceGroup = readEnvironmentVariable('SHOPFORGE_REGISTRY_RESOURCE_GROUP')
param storeHostSuffix = readEnvironmentVariable('SHOPFORGE_STORE_HOST_SUFFIX')
param adminHost = readEnvironmentVariable('SHOPFORGE_ADMIN_HOST', '')
param firstOperatorEmail = readEnvironmentVariable('SHOPFORGE_FIRST_OPERATOR', '')
param emailProvider = readEnvironmentVariable('SHOPFORGE_EMAIL_PROVIDER', 'mailgun')
param emailSenderAddress = readEnvironmentVariable('SHOPFORGE_EMAIL_SENDER')
param mailgunDomain = readEnvironmentVariable('SHOPFORGE_MAILGUN_DOMAIN')
param databasePassword = readEnvironmentVariable('SHOPFORGE_DB_PASSWORD')
param mailgunApiKey = readEnvironmentVariable('SHOPFORGE_MAILGUN_API_KEY')
param mailgunWebhookSigningKey = readEnvironmentVariable('SHOPFORGE_MAILGUN_WEBHOOK_SIGNING_KEY')
param stripeSecretKey = readEnvironmentVariable('SHOPFORGE_STRIPE_SECRET_KEY', '')
param stripeWebhookSecret = readEnvironmentVariable('SHOPFORGE_STRIPE_WEBHOOK_SECRET', '')
