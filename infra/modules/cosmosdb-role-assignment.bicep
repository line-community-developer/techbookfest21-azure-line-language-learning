@description('Cosmos DB account name')
param cosmosAccountName string

@description('Functions App Managed Identity Principal ID')
param functionsPrincipalId string

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-11-15' existing = {
  name: cosmosAccountName
}

// Built-in Cosmos DB Data Contributor role definition
var cosmosDbDataContributorRoleId = '00000000-0000-0000-0000-000000000002'

// Role assignment for Functions to access Cosmos DB
resource roleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-11-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, functionsPrincipalId, cosmosDbDataContributorRoleId)
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${cosmosDbDataContributorRoleId}'
    principalId: functionsPrincipalId
    scope: cosmosAccount.id
  }
}

output roleAssignmentId string = roleAssignment.id
