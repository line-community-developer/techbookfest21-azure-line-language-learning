@description('Functions App name')
param name string

@description('Location for the Functions App')
param location string

@description('Storage account name for Functions')
param storageAccountName string

@description('Application Insights connection string')
param appInsightsConnectionString string

@description('Static Web App default hostname for CORS')
param swaDefaultHostname string

@description('Cosmos DB endpoint')
param cosmosDbEndpoint string

@description('Cosmos DB database name')
param cosmosDbDatabaseName string

@description('Enable VNet integration')
param enableVNet bool = false

@description('VNet subnet ID for Functions (required if enableVNet is true)')
param vnetSubnetId string = ''

@description('Functions runtime name')
param functionsRuntimeName string

@description('Functions runtime version')
param functionsRuntimeVersion string

param aiEndpoint string
param foundryProjectEndpoint string

// Storage Account for Functions
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

// Blob Service for deployment package container
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

// Deployment package container
resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'deploymentpackage'
  properties: {
    publicAccess: 'None'
  }
}

// App Service Plan (Flex Consumption)
resource hostingPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${name}-plan'
  location: location
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true // Required for Linux
  }
}

// Azure Functions App
resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: name
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: hostingPlan.id
    reserved: true
    virtualNetworkSubnetId: enableVNet ? vnetSubnetId : null
    vnetContentShareEnabled: enableVNet
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storageAccount.properties.primaryEndpoints.blob}deploymentpackage'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
      runtime: {
        name: functionsRuntimeName
        version: functionsRuntimeVersion
      }
    }
    siteConfig: {
      appSettings: [
        { name: 'Learning__AiProvider', value: 'Foundry' }
        { name: 'Learning__TutorAgent', value: 'Unavailable' } // Enable after registering a saved agent version.
        { name: 'Learning__Retrieval', value: 'Local' }
        { name: 'Learning__Foundry__Endpoint', value: aiEndpoint }
        { name: 'Learning__Foundry__ProjectEndpoint', value: foundryProjectEndpoint }
        { name: 'Learning__Foundry__Model', value: 'learning' }
        { name: 'Learning__Foundry__JudgeModel', value: 'learning' }
        { name: 'Learning__Foundry__AgentName', value: 'learning-tutor' }
        { name: 'Learning__Line__TutorIconBaseUrl', value: '' }
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccount.name
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'CosmosDBConnection__accountEndpoint'
          value: cosmosDbEndpoint
        }
        {
          name: 'COSMOS_DB_DATABASE_NAME'
          value: cosmosDbDatabaseName
        }
      ]
      cors: {
        allowedOrigins: [
          'https://${swaDefaultHostname}'
        ]
      }
      // LINE and Foundry reach public HTTPS; each endpoint authenticates its caller.
      ipSecurityRestrictions: []
      ipSecurityRestrictionsDefaultAction: 'Allow'
    }
    httpsOnly: true
  }
}

// Role Assignment: Storage Blob Data Contributor
resource blobDataContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(functionApp.id, storageAccount.id, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output id string = functionApp.id
output name string = functionApp.name
output defaultHostname string = functionApp.properties.defaultHostName
output principalId string = functionApp.identity.principalId

var storageQueueDataContributor = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var storageTableDataContributor = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'

resource queueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(functionApp.id, storageAccount.id, storageQueueDataContributor)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageQueueDataContributor)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource tableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(functionApp.id, storageAccount.id, storageTableDataContributor)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributor)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}
