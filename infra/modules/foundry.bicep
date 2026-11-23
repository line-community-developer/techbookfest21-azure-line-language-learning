targetScope = 'resourceGroup'

param accountName string
param location string
param functionPrincipalId string

resource foundry 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: accountName
  location: location
  kind: 'AIServices'
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'S0'
  }
  properties: {
    allowProjectManagement: true
    customSubDomainName: accountName
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: foundry
  name: 'learning'
  location: location
  // Cognitive Services serializes account operations; wait for model provisioning.
  dependsOn: [
    model
  ]
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    displayName: 'Language Learning'
    description: 'Language Learning sample project for Microsoft Foundry'
  }
}

output projectId string = project.id

var agentConsumerRoleId = 'eed3b665-ab3a-47b6-8f48-c9382fb1dad6'

resource agentCaller 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(project.id, functionPrincipalId, agentConsumerRoleId)
  scope: project
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', agentConsumerRoleId)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output projectEndpoint string = 'https://${accountName}.services.ai.azure.com/api/projects/learning'

param modelName string
param modelVersion string
param modelCapacity int
var deploymentName = 'learning'
var foundryUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'

resource model 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundry
  name: deploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: modelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
    versionUpgradeOption: 'OnceCurrentVersionExpired'
  }
}

resource modelCaller 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundry.id, functionPrincipalId, foundryUserRoleId)
  scope: foundry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', foundryUserRoleId)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}


output accountName string = foundry.name
output modelDeploymentName string = model.name
