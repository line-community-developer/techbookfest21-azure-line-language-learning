targetScope = 'resourceGroup'

@description('Project name')
@minLength(3)
@maxLength(30)
param projectName string

@description('Location for Functions and Cosmos DB')
param location string = resourceGroup().location

@description('Location for Static Web App (must be explicitly provided)')
param swaLocation string

@description('Static Web Apps hosting plan')
@allowed([
  'Free'
  'Standard'
])
param staticWebAppSku string = 'Free'

@description('Cosmos DB mode')
@allowed(['freetier', 'serverless'])
param cosmosDbMode string = 'serverless'

@description('Enable VNet integration')
param enableVNet bool = true

@description('Foundry region with model quota')
param aiLocation string = location
param modelName string = 'gpt-5.4-mini'
param modelVersion string = '2026-03-17'
@minValue(1)
param modelCapacity int = 1

var aiAccountName = 'ai-${take(projectName, 35)}-${uniqueString(resourceGroup().id, projectName)}'

// Shared Log Analytics Workspace (in Functions region for data residency)
module logAnalytics 'modules/loganalytics.bicep' = {
  name: 'logAnalytics'
  params: {
    name: 'log-${projectName}'
    location: location
  }
}

// Application Insights for Static Web App (must be in same region as SWA)
module appInsightsSwa 'modules/appinsights.bicep' = {
  name: 'appInsightsSwa'
  params: {
    name: 'appi-${projectName}-swa'
    location: swaLocation
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
  }
}

// Application Insights for Functions (in same region as Functions)
module appInsightsFunctions 'modules/appinsights.bicep' = {
  name: 'appInsightsFunctions'
  params: {
    name: 'appi-${projectName}-func'
    location: location
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
  }
}

// Static Web App
module staticWebApp 'modules/staticwebapp.bicep' = {
  name: 'staticWebApp'
  params: {
    name: 'swa-${projectName}'
    location: swaLocation
    sku: staticWebAppSku
    appInsightsConnectionString: appInsightsSwa.outputs.connectionString
  }
}

// VNet (conditional)
module vnet 'modules/vnet.bicep' = if (enableVNet) {
  name: 'vnet'
  params: {
    name: 'vnet-${projectName}'
    location: location
  }
}

// Cosmos DB (conditional based on mode) - Deploy BEFORE Functions
module cosmosDbFreeTier 'modules/cosmosdb-freetier.bicep' = if (cosmosDbMode == 'freetier') {
  name: 'cosmosDb'
  params: {
    accountName: 'cosmos-${projectName}'
    databaseName: '${projectName}Database'
    location: location
    publicNetworkAccess: enableVNet ? 'Disabled' : 'Enabled'
  }
}

module cosmosDbServerless 'modules/cosmosdb-serverless.bicep' = if (cosmosDbMode == 'serverless') {
  name: 'cosmosDb'
  params: {
    accountName: 'cosmos-${projectName}'
    databaseName: '${projectName}Database'
    location: location
    publicNetworkAccess: enableVNet ? 'Disabled' : 'Enabled'
  }
}

// Cosmos DB Private Endpoint (conditional)
module cosmosPrivateEndpoint 'modules/private-endpoint-cosmos.bicep' = if (enableVNet) {
  name: 'cosmosPrivateEndpoint'
  params: {
    name: 'pe-cosmos-${projectName}'
    location: location
    cosmosAccountId: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.id : cosmosDbServerless!.outputs.id
    cosmosAccountName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.accountName : cosmosDbServerless!.outputs.accountName
    subnetId: vnet!.outputs.privateEndpointSubnetId
    vnetId: vnet!.outputs.id
  }
}

// Azure Functions (Flex Consumption) - Deploy AFTER Cosmos DB
module functionsFlex 'modules/functions-flex.bicep' = {
  name: 'functionsApp'
    params: {
      name: 'func-${projectName}'
      location: location
      storageAccountName: 'stg${uniqueString(resourceGroup().id, projectName)}'
      appInsightsConnectionString: appInsightsFunctions.outputs.connectionString
      swaDefaultHostname: staticWebApp.outputs.defaultHostname
      cosmosDbEndpoint: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.endpoint : cosmosDbServerless!.outputs.endpoint
      cosmosDbDatabaseName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.databaseName : cosmosDbServerless!.outputs.databaseName
      aiEndpoint: 'https://${aiAccountName}.openai.azure.com'
      foundryProjectEndpoint: 'https://${aiAccountName}.services.ai.azure.com/api/projects/learning'
      functionsRuntimeName: 'dotnet-isolated'
      functionsRuntimeVersion: '10.0'
      enableVNet: enableVNet
      vnetSubnetId: enableVNet ? vnet!.outputs.functionsSubnetId : ''
  }
  dependsOn: [
    cosmosPrivateEndpoint
  ]
}

// Cosmos DB role assignment for Functions (after Functions is created)
module cosmosDbRoleAssignmentFreeTier 'modules/cosmosdb-role-assignment.bicep' = if (cosmosDbMode == 'freetier') {
  name: 'cosmosDbRoleAssignment'
  params: {
    cosmosAccountName: cosmosDbFreeTier!.outputs.accountName
    functionsPrincipalId: functionsFlex.outputs.principalId
  }
}

module cosmosDbRoleAssignmentServerless 'modules/cosmosdb-role-assignment.bicep' = if (cosmosDbMode == 'serverless') {
  name: 'cosmosDbRoleAssignment'
  params: {
    cosmosAccountName: cosmosDbServerless!.outputs.accountName
    functionsPrincipalId: functionsFlex.outputs.principalId
  }
}

// Update SWA config with Functions hostname (after Functions deployment)
module staticWebAppConfig 'modules/staticwebapp-config.bicep' = {
  name: 'staticWebAppConfig'
  params: {
    staticWebAppName: staticWebApp.outputs.name
    functionsDefaultHostname: functionsFlex.outputs.defaultHostname
    functionsAppName: functionsFlex.outputs.name
    appInsightsConnectionString: appInsightsSwa.outputs.connectionString
  }
}

output staticWebAppName string = staticWebApp.outputs.name
output staticWebAppUrl string = staticWebApp.outputs.defaultHostname
output functionsAppName string = functionsFlex.outputs.name
output functionsAppUrl string = functionsFlex.outputs.defaultHostname
output cosmosDbAccountName string = cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.accountName : cosmosDbServerless!.outputs.accountName
output cosmosDbEndpoint string = cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.endpoint : cosmosDbServerless!.outputs.endpoint
output cosmosDatabaseName string = cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.databaseName : cosmosDbServerless!.outputs.databaseName
output logAnalyticsWorkspaceName string = logAnalytics.outputs.name
output logAnalyticsWorkspaceId string = logAnalytics.outputs.id
output appInsightsSwaName string = appInsightsSwa.outputs.name
output appInsightsSwaConnectionString string = appInsightsSwa.outputs.connectionString
output appInsightsFunctionsName string = appInsightsFunctions.outputs.name
output appInsightsFunctionsConnectionString string = appInsightsFunctions.outputs.connectionString
output vnetEnabled bool = enableVNet
output vnetName string = enableVNet ? vnet!.outputs.name : ''

module learningProfileContainer 'containers/learning-profile-container.bicep' = {
  name: 'learning-profile-container'
  params: {
    cosmosAccountName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.accountName : cosmosDbServerless!.outputs.accountName
    databaseName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.databaseName : cosmosDbServerless!.outputs.databaseName
  }
}

module learningObservationContainer 'containers/learning-observation-container.bicep' = {
  name: 'learning-observation-container'
  params: {
    cosmosAccountName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.accountName : cosmosDbServerless!.outputs.accountName
    databaseName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.databaseName : cosmosDbServerless!.outputs.databaseName
  }
}

module practiceSessionContainer 'containers/practice-session-container.bicep' = {
  name: 'practice-session-container'
  params: {
    cosmosAccountName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.accountName : cosmosDbServerless!.outputs.accountName
    databaseName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.databaseName : cosmosDbServerless!.outputs.databaseName
  }
}

module tutorSessionContainer 'containers/tutor-session-container.bicep' = {
  name: 'tutor-session-container'
  params: {
    cosmosAccountName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.accountName : cosmosDbServerless!.outputs.accountName
    databaseName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.databaseName : cosmosDbServerless!.outputs.databaseName
  }
}

module reviewPlanContainer 'containers/review-plan-container.bicep' = {
  name: 'review-plan-container'
  params: {
    cosmosAccountName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.accountName : cosmosDbServerless!.outputs.accountName
    databaseName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.databaseName : cosmosDbServerless!.outputs.databaseName
  }
}

module workflowRunContainer 'containers/workflow-run-container.bicep' = {
  name: 'workflow-run-container'
  params: {
    cosmosAccountName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.accountName : cosmosDbServerless!.outputs.accountName
    databaseName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.databaseName : cosmosDbServerless!.outputs.databaseName
  }
}

module processedEventContainer 'containers/processed-event-container.bicep' = {
  name: 'processed-event-container'
  params: {
    cosmosAccountName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.accountName : cosmosDbServerless!.outputs.accountName
    databaseName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.databaseName : cosmosDbServerless!.outputs.databaseName
  }
}

module grammarTopicContainer 'containers/grammar-topic-container.bicep' = {
  name: 'grammar-topic-container'
  params: {
    cosmosAccountName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.accountName : cosmosDbServerless!.outputs.accountName
    databaseName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.databaseName : cosmosDbServerless!.outputs.databaseName
  }
}

module tutorProfileContainer 'containers/tutor-profile-container.bicep' = {
  name: 'tutor-profile-container'
  params: {
    cosmosAccountName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.accountName : cosmosDbServerless!.outputs.accountName
    databaseName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.databaseName : cosmosDbServerless!.outputs.databaseName
  }
}

module sampleQuestionContainer 'containers/sample-question-container.bicep' = {
  name: 'sample-question-container'
  params: {
    cosmosAccountName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.accountName : cosmosDbServerless!.outputs.accountName
    databaseName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.databaseName : cosmosDbServerless!.outputs.databaseName
  }
}

module tutorToolTurnContainer 'containers/tutor-tool-turn-container.bicep' = {
  name: 'tutor-tool-turn-container'
  params: {
    cosmosAccountName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.accountName : cosmosDbServerless!.outputs.accountName
    databaseName: cosmosDbMode == 'freetier' ? cosmosDbFreeTier!.outputs.databaseName : cosmosDbServerless!.outputs.databaseName
  }
}

// Create the final Foundry account directly (no OpenAI-to-AIServices upgrade).
module foundry 'modules/foundry.bicep' = {
  name: 'foundry'
  params: {
    accountName: aiAccountName
    location: aiLocation
    functionPrincipalId: functionsFlex.outputs.principalId
    modelName: modelName
    modelVersion: modelVersion
    modelCapacity: modelCapacity
  }
}

output foundryAccountName string = foundry.outputs.accountName
output foundryProjectEndpoint string = foundry.outputs.projectEndpoint
output modelDeploymentName string = foundry.outputs.modelDeploymentName
