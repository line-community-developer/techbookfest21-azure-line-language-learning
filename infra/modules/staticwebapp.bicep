@description('Static Web App name')
param name string

@description('Location for the Static Web App')
param location string

@description('SKU name (Free or Standard)')
@allowed([
  'Free'
  'Standard'
])
param sku string = 'Standard'

@description('Application Insights connection string')
param appInsightsConnectionString string

resource staticWebApp 'Microsoft.Web/staticSites@2023-01-01' = {
  name: name
  location: location
  sku: {
    name: sku
    tier: sku
  }
  properties: {
    buildProperties: {
      skipGithubActionWorkflowGeneration: true
    }
  }
}

// Link Application Insights to Static Web App (for both client and server-side telemetry)
resource staticWebAppConfig 'Microsoft.Web/staticSites/config@2023-01-01' = {
  parent: staticWebApp
  name: 'appsettings'
  properties: {
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsightsConnectionString
    ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
  }
}

output id string = staticWebApp.id
output name string = staticWebApp.name
output defaultHostname string = staticWebApp.properties.defaultHostname
