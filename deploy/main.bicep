param location string = resourceGroup().location
param managedIdentityName string = 'id-secrets-rotation-001'
param keyVaultName string = 'kv-secrets-rotation-001'
param keyVaultSku string = 'standard'
param functionAppName string = 'func-secrets-rotation-001'
param queueName string = 'kv-secrets-near-expiry'
param functionAppPlanName string = 'plan-secrets-rotation-001'
param appInsightsName string = 'appi-${functionAppName}'
param topicName string = 'topic-secrets-rotation-001'
param eventSubscriptionName string = 'sub-secrets-rotation'
param storageAccountName string = 'stgsecretsrotation001'
param storageSku string = 'Standard_LRS'
param functionRuntime string = 'dotnet'

// Create the managed identity
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: managedIdentityName
  location: location
}

// Create the storage account for the Function App and the related file share and queue
resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: storageSku
  }
  kind: 'StorageV2'
  properties: {
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
    encryption: {
      services: {
        file: {
          keyType: 'Account'
          enabled: true
        }
        blob: {
          keyType: 'Account'
          enabled: true
        }
      }
      keySource: 'Microsoft.Storage'
    }
    accessTier: 'Hot'
  }
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2022-09-01' = {
  name: 'default'
  parent: storageAccount
  resource queue 'queues@2022-09-01' = {
    name: queueName
  }
}

/* The following role assignments are required for the Function App to be able
    to access the storage account using only User Assigned Managed Identity */  

// Assign the ManagedIdentity the role 'Storage Account Contributor'  to the storage account
resource storageAccountContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, managedIdentity.id, 'Storage Account Contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '17d1049b-9a84-46fb-8f53-869881c3d3ab') 
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Assign the ManagedIdentity the role 'Storage Blob Data Owner' to the storage account
resource blobDataOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, managedIdentity.id, 'Storage Blob Data Owner')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b') 
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Assign the ManagedIdentity the role 'Storage Queue Data Contributor' to the storage account
resource queueDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, managedIdentity.id, 'Storage Queue Data Contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88') 
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Create the Key vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      name: keyVaultSku
      family: 'A'
    }
    enableRbacAuthorization: true
  }
}

// Assign the role 'Key vault secrets officer' to the managed identity
resource keyVaultSecretsOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, managedIdentity.id, 'key-vault-secrets-officer')
  scope: keyVault
  properties: {
    principalId: managedIdentity.properties.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalType: 'ServicePrincipal'
  }
}

// Create the Application Insights for the Function App monitoring
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

// Create the Azure Function Hosting Plan
resource functionAppPlan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: functionAppPlanName
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
}

// Create the Azure Function App
resource functionApp 'Microsoft.Web/sites@2022-09-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  dependsOn:[keyVaultSecretsOfficer, blobDataOwner, storageAccountContributor, queueDataContributor]
  properties: {
    serverFarmId: functionAppPlan.id
    siteConfig: {
      netFrameworkVersion: 'v6.0'
      use32BitWorkerProcess: false
      ftpsState: 'Disabled'
      appSettings: [        
        {
          // Indicates that the Function App is using a managed identity
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccount.name
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsights.properties.InstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: 'InstrumentationKey=${appInsights.properties.InstrumentationKey}'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: functionRuntime
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          // Indicates that the Function App is using a user assigned managed identity with the below ClientId
          name:'AZURE_CLIENT_ID'
          value: managedIdentity.properties.clientId
        }
        {
          name: 'ManagedIdentityClientId'
          value: managedIdentity.properties.clientId
        }
      ]
    }
    httpsOnly: true
    keyVaultReferenceIdentity: managedIdentity.id
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
  }
}

// Create the topic for the Key Vault events
resource keyVaultTopic 'Microsoft.EventGrid/systemTopics@2022-06-15' = {
  name: topicName
  location: location
  properties:{
    source: keyVault.id
    topicType: 'Microsoft.KeyVault.vaults'
  }
  identity: {
    type: 'SystemAssigned'
  }
}

// Assign the role 'Storage Queue Data Message Sender' to the topic
resource queueDataMessageSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, keyVaultTopic.id, 'Storage Queue Data Message Sender')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'c6a89b2d-59bc-44d0-9896-0f6e12d7b80a') 
    principalId: keyVaultTopic.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Create the subscription for the Key Vault events: SecretNearExpiry & SecretExpired
resource keyVaultEventSubscription 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2022-06-15' = {
  parent: keyVaultTopic
  name: eventSubscriptionName
  dependsOn:[queueDataMessageSender]
  properties: {
    destination: {
      endpointType: 'StorageQueue'
      properties: {
        resourceId: storageAccount.id
        queueName: queueService::queue.name       
      }
    }
    eventDeliverySchema: 'EventGridSchema'
    filter: {
      includedEventTypes: [
        'Microsoft.KeyVault.SecretNearExpiry'
        'Microsoft.KeyVault.SecretExpired'
      ]
    }
  }
}

output managedIdentityClientId string = managedIdentity.properties.clientId
output managedIdentityPrincipalId string = managedIdentity.properties.principalId
